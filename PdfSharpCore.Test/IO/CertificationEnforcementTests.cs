using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.AcroForms;
using PdfSharpCore.Pdf.Annotations;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Signatures;
using PdfSharpCore.Signing;
using PdfSharpCore.Test.Helpers;
using PdfSharpCore.Test.Pdfs.AcroForms;
using Xunit;
using Reader = PdfSharpCore.Pdf.IO.PdfReader;

namespace PdfSharpCore.Test.IO;

/// <summary>
///   A certifying signature's <c>/DocMDP</c> level, enforced. The library used to write the level and
///   honour none of it: the same document reopened could be changed however the caller liked. This is
///   the second dimension of the matrix <see cref="OpenModeEnforcementTests"/> already pins, because
///   the guard both refusals go through is now the same one — <c>PdfDocument.EnsureCanModify</c>
///   widened to ask not just "may this document be changed" but "may it take <em>this kind</em> of
///   change". A mode refusal and a certification refusal are asserted on their message as well as
///   their type, for the same reason the mode matrix is: the point of the change is that the refusal
///   names which of the two applies.
/// </summary>
public class CertificationEnforcementTests
{
    [Theory]
    [InlineData(PdfCertificationLevel.NoChangesAllowed)]
    [InlineData(PdfCertificationLevel.FormFillingAllowed)]
    [InlineData(PdfCertificationLevel.FormFillingAndAnnotationsAllowed)]
    public void DocumentStructureIsRefusedAtEveryCertificationLevel(PdfCertificationLevel level)
    {
        var document = OpenedForAppend(Certified(UnsignedWithAField(), level));

        Action act = () => document.AddPage();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*PdfCertificationLevel.{level}*")
            .And.Message.Should().Contain("adding a page");
    }

    [Fact]
    public void NoChangesAllowedRefusesFillingAFieldToo()
    {
        var document = OpenedForAppend(Certified(UnsignedWithAField(), PdfCertificationLevel.NoChangesAllowed));

        Action act = () => Field(document).Value = new PdfString("filled");

        act.Should().Throw<InvalidOperationException>().WithMessage("*NoChangesAllowed*");
    }

    [Fact]
    public void NoChangesAllowedRefusesAddingAnAnnotationToo()
    {
        var document = OpenedForAppend(Certified(UnsignedWithAField(), PdfCertificationLevel.NoChangesAllowed));

        Action act = () => document.Pages[0].Annotations.Add(new PdfTextAnnotation());

        act.Should().Throw<InvalidOperationException>().WithMessage("*NoChangesAllowed*");
    }

    [Fact]
    public void FormFillingAllowedPermitsFillingAFieldAndRefusesAnnotations()
    {
        var document = OpenedForAppend(Certified(UnsignedWithAField(), PdfCertificationLevel.FormFillingAllowed));

        Action filling = () => Field(document).Value = new PdfString("filled");
        filling.Should().NotThrow();

        Action annotating = () => document.Pages[0].Annotations.Add(new PdfTextAnnotation());
        annotating.Should().Throw<InvalidOperationException>().WithMessage("*FormFillingAllowed*");
    }

    [Fact]
    public void FormFillingAllowedPermitsSigningAgain()
    {
        var certified = Certified(UnsignedWithAField(), PdfCertificationLevel.FormFillingAllowed);

        Action signingAgain = () => Sign(certified, new PdfSignatureOptions { FieldName = "Signature2" });

        signingAgain.Should().NotThrow();
    }

    [Fact]
    public void FormFillingAndAnnotationsAllowedPermitsBothAndStillRefusesThePageTree()
    {
        var document = OpenedForAppend(
            Certified(UnsignedWithAField(), PdfCertificationLevel.FormFillingAndAnnotationsAllowed));

        Action filling = () => Field(document).Value = new PdfString("filled");
        filling.Should().NotThrow();

        Action annotating = () => document.Pages[0].Annotations.Add(new PdfTextAnnotation());
        annotating.Should().NotThrow();

        Action addingAPage = () => document.AddPage();
        addingAPage.Should().Throw<InvalidOperationException>()
            .WithMessage("*FormFillingAndAnnotationsAllowed*");
    }

    [Fact]
    public void ARefusalByCertificationNamesCertificationRatherThanTheMode()
    {
        var document = OpenedForAppend(Certified(UnsignedWithAField(), PdfCertificationLevel.NoChangesAllowed));

        Action act = () => document.AddPage();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*certified*")
            .And.Message.Should().NotContain("PdfDocumentOpenMode");
    }

    [Fact]
    public void ADocumentThatFailsBothIsRefusedOnceByTheModeItWasOpenedWith()
    {
        var certified = Certified(UnsignedWithAField(), PdfCertificationLevel.NoChangesAllowed);
        var document = Reader.Open(new MemoryStream(certified), PdfDocumentOpenMode.ReadOnly);

        Action act = () => document.AddPage();

        // Refused once, for the mode - the more fundamental of the two reasons, and the one to fix
        // first. The certification refusal, worded differently, is never reached.
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PdfDocumentOpenMode.ReadOnly*")
            .And.Message.Should().NotContain("PdfCertificationLevel");
    }

    [Fact]
    public void AFullSaveOfACertifiedDocumentIsRefused()
    {
        var document = OpenedForAppend(Certified(UnsignedWithAField(), PdfCertificationLevel.FormFillingAllowed));

        Action act = () => document.Save(new MemoryStream(), false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*FormFillingAllowed*");
    }

    [Fact]
    public void AnIncrementalSaveOfAPermittedChangeToACertifiedDocumentSucceeds()
    {
        var document = OpenedForAppend(Certified(UnsignedWithAField(), PdfCertificationLevel.FormFillingAllowed));
        Field(document).Value = new PdfString("filled");

        Action act = () => document.SaveIncremental(new MemoryStream());

        act.Should().NotThrow();
    }

    [Fact]
    public void AnUnsignedDocumentIsUnaffectedAcrossTheWholeMatrix()
    {
        var document = OpenedForAppend(UnsignedWithAField());

        Field(document).Value = new PdfString("filled");
        document.Pages[0].Annotations.Add(new PdfTextAnnotation());
        document.AddPage();

        Action act = () => document.Save(new MemoryStream(), false);
        act.Should().NotThrow();
    }

    static PdfAcroField Field(PdfDocument document) =>
        document.Internals.Catalog.AcroForm.Fields[0];

    static byte[] UnsignedWithAField()
    {
        var opened = new AcroFormBuilder().With("/Tx", "Field1").Build();

        using var output = new MemoryStream();
        opened.Save(output, false);
        return output.ToArray();
    }

    static byte[] Certified(byte[] document, PdfCertificationLevel level) =>
        Sign(document, new PdfSignatureOptions { Certification = level });

    static byte[] Sign(byte[] document, PdfSignatureOptions options)
    {
        using var input = new MemoryStream(document);
        using var output = new MemoryStream();

        PdfSigner.Sign(input, output, new Pkcs7Signer(SigningCertificates.Default), options);
        return output.ToArray();
    }

    static PdfDocument OpenedForAppend(byte[] document) =>
        Reader.Open(new MemoryStream(document), PdfDocumentOpenMode.Append);
}
