using System;
using System.IO;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;
using static PdfSharpCore.Test.IO.ResourceConformanceFixtures;

namespace PdfSharpCore.Test.IO;

/// <summary>
///   Four PDF/A rules the conformance writer admitted, in its own comments, that it could not
///   check: no transparency and no JPEG 2000 image under PDF/A-1, no interpolated image under any
///   archival profile, and no device colour the output intent does not describe. Answering any of
///   them means walking every page's resources, which is <see cref="Advanced.PdfResourcePruner"/>'s
///   job as well — lifted into <see cref="Advanced.PdfPageResourceUsage"/> so the two cannot
///   disagree about what a page uses.
/// </summary>
public class ResourceConformanceRulesTests
{
    [Fact]
    public void ATranslucentGraphicsStateBreaksPdfA1()
    {
        var saving = Saving(PdfAConformance.PdfA1B, PageWithATranslucentGraphicsState());

        saving.Should().Throw<InvalidOperationException>().WithMessage("*PDF/A-1*transparency*");
    }

    [Fact]
    public void TheSameTranslucentGraphicsStateIsFineUnderPdfA2()
    {
        var saving = Saving(PdfAConformance.PdfA2B, PageWithATranslucentGraphicsState());

        saving.Should().NotThrow();
    }

    [Fact]
    public void AnExplicitlyOpaqueGraphicsStateDoesNotBreakPdfA1()
    {
        // An explicit /ca 1 is opaque, not merely absent — the rule is about what is painted, not
        // about whether a graphics state happens to mention alpha at all.
        var saving = Saving(PdfAConformance.PdfA1B, PageWithAnOpaqueGraphicsState());

        saving.Should().NotThrow();
    }

    [Fact]
    public void TransparencyReachedOnlyThroughANestedFormIsStillFound()
    {
        // The case a shallower walk would miss: the page's own resource dictionary names only a
        // form, and the transparency group is declared on the form the page draws through it.
        var saving = Saving(PdfAConformance.PdfA1B, PageWithTransparencyThroughANestedForm());

        saving.Should().Throw<InvalidOperationException>().WithMessage("*PDF/A-1*transparency*");
    }

    [Fact]
    public void TransparencyReachedOnlyThroughASoftMaskIsStillFound()
    {
        var saving = Saving(PdfAConformance.PdfA1B, PageWithTransparencyThroughASoftMask());

        saving.Should().Throw<InvalidOperationException>().WithMessage("*PDF/A-1*transparency*");
    }

    [Fact]
    public void AJpeg2000ImageBreaksPdfA1()
    {
        var saving = Saving(PdfAConformance.PdfA1B, PageWithAJpeg2000Image());

        saving.Should().Throw<InvalidOperationException>().WithMessage("*PDF/A-1*JPEG 2000*");
    }

    [Fact]
    public void TheSameJpeg2000ImageIsFineUnderPdfA2()
    {
        var saving = Saving(PdfAConformance.PdfA2B, PageWithAJpeg2000Image());

        saving.Should().NotThrow();
    }

    [Fact]
    public void AnOrdinaryImageDoesNotBreakPdfA1()
    {
        var saving = Saving(PdfAConformance.PdfA1B, PageWithAnOrdinaryImage());

        saving.Should().NotThrow();
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA1B)]
    [InlineData(PdfAConformance.PdfA2B)]
    [InlineData(PdfAConformance.PdfA3B)]
    public void AnInterpolatedImageBreaksEveryArchivalProfile(PdfAConformance conformance)
    {
        var saving = Saving(conformance, PageWithAnInterpolatedImage());

        saving.Should().Throw<InvalidOperationException>().WithMessage("*interpolate*");
    }

    [Fact]
    public void MixingDeviceColourSpacesAgainstTheOutputIntentIsRefused()
    {
        // The default output intent this document is given is sRGB, a 3-component space, and the
        // page also paints with 4-component CMYK directly.
        var saving = Saving(PdfAConformance.PdfA2B, PageMixingDeviceColourSpaces());

        saving.Should().Throw<InvalidOperationException>()
            .WithMessage("*output intent*").WithMessage("*3-component*").WithMessage("*4-component*");
    }

    [Fact]
    public void PaintingOnlyInTheColourTheOutputIntentDescribesIsFine()
    {
        var saving = Saving(PdfAConformance.PdfA2B, PageDrawingOnlyRgb());

        saving.Should().NotThrow();
    }

    [Fact]
    public void ADocumentClaimingNothingIsUnaffectedByAnyOfThis()
    {
        // The whole point of walking only when a profile is claimed: a document that never asks for
        // conformance pays nothing for it, however its pages are shaped.
        var saving = Saving(PdfAConformance.None, PageMixingDeviceColourSpaces());

        saving.Should().NotThrow();
    }

    private static Action Saving(PdfAConformance conformance, byte[] rawDocument)
    {
        return () =>
        {
            var document = PdfSharpCore.Pdf.IO.PdfReader.Open(
                new MemoryStream(rawDocument), PdfDocumentOpenMode.Modify);
            document.Info.Title = "Resource conformance fixture";

            // RawPdf.Build always writes a PDF 1.7 header, and PDF/A-1 is defined against PDF 1.4 —
            // a rule these fixtures are not about, so it is settled before the claim rather than
            // left to shadow the resource-rule failure each of them exists to exercise.
            document.Version = 14;

            document.Options.Conformance = conformance;

            using var output = new MemoryStream();
            document.Save(output, false);
        };
    }
}
