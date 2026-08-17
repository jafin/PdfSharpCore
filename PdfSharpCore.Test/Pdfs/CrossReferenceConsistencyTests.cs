using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

// This namespace has a PdfReader of its own, so the one that opens documents needs saying in full.
using Reader = PdfSharpCore.Pdf.IO.PdfReader;

namespace PdfSharpCore.Test.Pdfs;

/// <summary>
///   The cross-reference table's own invariants: every object numbered once, every number in the
///   xref backed by an object, and the count the xref declares matching what is actually there.
/// </summary>
/// <remarks>
///   <para>
///   <c>PdfCrossReferenceTable</c> used to carry a <c>CheckConsistence</c> that asserted these with
///   <c>Debug.Assert</c>, called from <c>Compact</c>, <c>Renumber</c>, <c>TransitiveClosure</c> and
///   from <c>PdfReader.Open</c>. It never ran: it was marked <c>[Conditional("DEBUG_")]</c>, and the
///   trailing underscore is how this codebase spells a symbol that is never defined, so the compiler
///   removed every call in Debug and Release alike. It was also quadratic in the number of objects,
///   which is why it could not simply be switched on.
///   </para>
///   <para>
///   These assert the same invariants from outside, against the bytes the writer produced, which is
///   where they are actually observable and where a consumer would feel them break. The renumbering
///   path matters most and is the one the old checks bracketed: importing pages renumbers every
///   object, and two objects sharing a number is a corrupt file rather than a wrong-looking one.
///   </para>
/// </remarks>
public class CrossReferenceConsistencyTests
{
    /// <summary>Every object number defined in the file, in the order the writer wrote them.</summary>
    static IReadOnlyList<int> ObjectNumbersIn(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        return Regex.Matches(text, @"(?m)^(\d+) (\d+) obj\b")
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToList();
    }

    /// <summary>The object count the xref section declares, which is one more than the objects.</summary>
    static int DeclaredXrefCount(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var match = Regex.Match(text, @"(?m)^xref\r?\n0 (\d+)\b");
        match.Success.Should().BeTrue("the file has to have an xref section to be a PDF");
        return int.Parse(match.Groups[1].Value);
    }

    static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    static byte[] ADocumentOf(int pages)
    {
        var document = new PdfDocument();
        for (var page = 0; page < pages; page++)
            document.AddPage();
        return Save(document);
    }

    static void ShouldBeConsistent(byte[] pdf)
    {
        var numbers = ObjectNumbersIn(pdf);

        numbers.Should().NotBeEmpty("a written document defines objects");
        numbers.Should().OnlyHaveUniqueItems("two objects sharing a number is a corrupt file");
        numbers.Should().AllSatisfy(number => number.Should().BeGreaterThan(0,
            "object zero is the free-list head and is never defined"));

        // The xref declares 0..n, so it counts one more entry than there are objects.
        DeclaredXrefCount(pdf).Should().Be(numbers.Count + 1,
            "the xref has to account for exactly the objects the file defines");
    }

    [Fact]
    public void AWrittenDocumentNumbersEveryObjectOnce()
    {
        ShouldBeConsistent(ADocumentOf(3));
    }

    [Fact]
    public void ADocumentOpenedAndWrittenAgainIsStillConsistent()
    {
        // PdfReader.Open in Modify mode compacts and renumbers, which is where the removed checks
        // were called from.
        var reopened = Reader.Open(new MemoryStream(ADocumentOf(3)), PdfDocumentOpenMode.Modify);

        ShouldBeConsistent(Save(reopened));
    }

    [Fact]
    public void ImportingPagesFromAnotherDocumentRenumbersWithoutCollision()
    {
        // The case the invariant exists for. Both documents number their objects from one, so
        // importing without renumbering would give two objects the same number.
        var source = Reader.Open(new MemoryStream(ADocumentOf(3)), PdfDocumentOpenMode.Import);
        var target = Reader.Open(new MemoryStream(ADocumentOf(2)), PdfDocumentOpenMode.Modify);

        foreach (var page in source.Pages)
            target.AddPage(page);

        var written = Save(target);

        ShouldBeConsistent(written);
        Reader.Open(new MemoryStream(written), PdfDocumentOpenMode.Modify).PageCount.Should().Be(5);
    }

    [Fact]
    public void RemovingAPageLeavesTheRemainingObjectsConsistent()
    {
        var document = Reader.Open(new MemoryStream(ADocumentOf(4)), PdfDocumentOpenMode.Modify);
        document.Pages.RemoveAt(1);

        var written = Save(document);

        ShouldBeConsistent(written);
        Reader.Open(new MemoryStream(written), PdfDocumentOpenMode.Modify).PageCount.Should().Be(3);
    }
}
