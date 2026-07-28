using System.IO;
using System.Linq;
using AwesomeAssertions;
using ImageMagick;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Test.Helpers;
using PdfSharpCore.Utils;
using Xunit;

namespace PdfSharpCore.Test.Fonts
{
    /// <summary>
    /// A font with PostScript (CFF) outlines has no 'glyf' and no 'loca' table, so it cannot be
    /// subsetted. The simple-font path used to walk into that and throw NullReferenceException; the
    /// CID path survived by embedding the whole OTTO file as '/FontFile2' under a '/CIDFontType2'
    /// descendant, which describes a TrueType program and is therefore a lie about what is in the
    /// stream. Such a font is now embedded whole, as '/FontFile3' with a subtype of '/OpenType'.
    /// </summary>
    [Collection(RasterizingCollection.Name)]
    public class PostscriptOutlineEmbeddingTest
    {
        [Fact]
        public void AFontWithPostscriptOutlinesIsEmbeddedAsAnOpenTypeProgram()
        {
            PdfDocument saved = DrawAndReopen(PdfFontEncoding.Unicode);

            PdfDictionary descriptor = FontDescriptorOf(saved);

            descriptor.Elements.ContainsKey("/FontFile3").Should()
                .BeTrue("PostScript outlines are an OpenType program, not a TrueType one");
            descriptor.Elements.ContainsKey("/FontFile2").Should()
                .BeFalse("'/FontFile2' is defined as a TrueType font program");

            PdfDictionary program = Resolve(saved, descriptor.Elements["/FontFile3"]);
            program.Elements.GetName("/Subtype").Should().Be("/OpenType");

            // '/Length1' belongs to a TrueType program; '/Subtype' takes its place here.
            program.Elements.ContainsKey("/Length1").Should().BeFalse();
        }

        /// <summary>
        /// PostScript outlines are not subsetted - that would mean rebuilding the charstrings and
        /// the subroutine sets - so the stream has to be the font, whole and unaltered. Checking
        /// the bytes is what says so; the dictionary keys only say what was intended.
        /// </summary>
        [Fact]
        public void TheEmbeddedProgramIsTheFontFileItself()
        {
            PdfDocument saved = DrawAndReopen(PdfFontEncoding.Unicode);

            PdfDictionary program = Resolve(saved, FontDescriptorOf(saved).Elements["/FontFile3"]);
            byte[] expected = File.ReadAllBytes(
                PathHelper.GetInstance().GetAssetPath("Fonts", "SourceCodePro-Regular.otf"));

            program.Stream.UnfilteredValue.Should().Equal(expected);
        }

        [Fact]
        public void TheDescendantOfAPostscriptFontIsACidFontType0()
        {
            PdfDocument saved = DrawAndReopen(PdfFontEncoding.Unicode);

            DescendantFontsOf(saved).Single()
                .Elements.GetName("/Subtype").Should().Be("/CIDFontType0",
                    "CIDFontType2 means glyf outlines, which this font does not have");
        }

        [Fact]
        public void EmbeddingAnOpenTypeProgramRaisesTheDocumentVersion()
        {
            // '/Subtype /OpenType' in a font program stream arrives in PDF 1.6.
            DrawAndReopen(PdfFontEncoding.Unicode).Version.Should().BeGreaterThanOrEqualTo(16);
        }

        [Fact]
        public void ASimpleFontWithPostscriptOutlinesEmbedsRatherThanThrowing()
        {
            // The path that used to raise NullReferenceException on a missing 'loca' table.
            PdfDocument saved = DrawAndReopen(PdfFontEncoding.WinAnsi);

            FontDescriptorOf(saved).Elements.ContainsKey("/FontFile3").Should().BeTrue();
        }

        /// <summary>
        /// TrueType fonts must be unaffected: still subsetted, still '/FontFile2'.
        /// </summary>
        [Fact]
        public void ATrueTypeFontIsStillSubsettedIntoAFontFile2()
        {
            PdfDocument saved = DrawAndReopen(PdfFontEncoding.Unicode, "Arial");

            PdfDictionary descriptor = FontDescriptorOf(saved);

            descriptor.Elements.ContainsKey("/FontFile2").Should().BeTrue();
            descriptor.Elements.ContainsKey("/FontFile3").Should().BeFalse();

            DescendantFontsOf(saved).Single()
                .Elements.GetName("/Subtype").Should().Be("/CIDFontType2");

            // The point of subsetting: what goes in is smaller than the font it came from.
            PdfDictionary program = Resolve(saved, descriptor.Elements["/FontFile2"]);
            long embedded = program.Elements.GetInteger("/Length1");
            long source = new FileInfo(
                PathHelper.GetInstance().GetAssetPath("Fonts", "LiberationSans-Regular.ttf")).Length;

            embedded.Should().BeLessThan(source);
        }

        /// <summary>
        /// The bytes being right does not make the file readable: the old output embedded this same
        /// font correctly enough that lenient viewers coped, and described it as something it was
        /// not, which strict readers reject. Ghostscript is the strict reader on hand.
        /// </summary>
        /// <remarks>
        /// This asserts that the page is drawn, not which glyphs are on it: Ghostscript substitutes
        /// silently for a font it cannot use, so ink alone does not prove the embedded program was
        /// the one drawn from. What it does prove is that a reader that refuses malformed font
        /// dictionaries accepts this one and renders the page - which is what the old output failed
        /// to do. The identity of the program is settled above, against its bytes.
        /// </remarks>
        [GoldenImageFact]
        public void GhostscriptRendersAPageCarryingAnEmbeddedOpenTypeProgram()
        {
            var rendered = PdfHelper.Rasterize(Draw(PdfFontEncoding.Unicode));

            using var page = rendered.ImageCollection.Single().Clone();
            page.ColorFuzz = new Percentage(20);
            page.Trim();

            page.Width.Should().BeGreaterThan(0, "the page must not come out blank");
            page.Height.Should().BeGreaterThan(0, "the page must not come out blank");
        }

        /// <summary>
        /// Discovery globbed '*.ttf' alone, so an .otf was invisible to the shipped resolver no
        /// matter what the embedding code could do with one.
        /// </summary>
        [Fact]
        public void AnOtfIsDiscoveredAndResolvedByTheShippedResolver()
        {
            var resolver = new Probe();
            resolver.SetupFontsFiles(new[]
            {
                PathHelper.GetInstance().GetAssetPath("Fonts", "SourceCodePro-Regular.otf"),
            });

            var info = resolver.ResolveTypeface("Source Code Pro", false, false);

            info.Should().NotBeNull();
            info.FaceName.Should().Be("SourceCodePro-Regular.otf");

            // 'OTTO' - the signature that says the outlines are PostScript rather than TrueType.
            resolver.GetFont(info.FaceName).Take(4).Should().Equal((byte)'O', (byte)'T', (byte)'T', (byte)'O');
        }

        private sealed class Probe : SkiaFontResolver
        {
            public Probe()
            {
                NullIfFontNotFound = true;
            }
        }

        private static PdfDocument Draw(PdfFontEncoding encoding,
            string familyName = PinnedFontResolver.CffFamilyName)
        {
            var document = new PdfDocument();
            var page = document.AddPage();

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var font = new XFont(familyName, 12, XFontStyle.Regular, new XPdfFontOptions(encoding));
                gfx.DrawString("Embedding check", font, XBrushes.Black, new XPoint(20, 40));
            }

            return document;
        }

        private static PdfDocument DrawAndReopen(PdfFontEncoding encoding,
            string familyName = PinnedFontResolver.CffFamilyName)
        {
            using var stream = new MemoryStream();
            Draw(encoding, familyName).Save(stream, false);
            stream.Position = 0;

            return Pdf.IO.PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        }

        private static PdfDictionary FontDescriptorOf(PdfDocument document)
        {
            return document.Internals.GetAllObjects()
                .OfType<PdfDictionary>()
                .Single(d => d.Elements.GetName("/Type") == "/FontDescriptor");
        }

        private static PdfDictionary[] DescendantFontsOf(PdfDocument document)
        {
            return document.Internals.GetAllObjects()
                .OfType<PdfDictionary>()
                .Where(d => d.Elements.GetName("/Type") == "/Font")
                .Where(d => d.Elements.ContainsKey("/CIDSystemInfo"))
                .ToArray();
        }

        private static PdfDictionary Resolve(PdfDocument document, PdfItem item)
        {
            return (PdfDictionary)(item is PdfReference reference ? reference.Value : item);
        }
    }
}
