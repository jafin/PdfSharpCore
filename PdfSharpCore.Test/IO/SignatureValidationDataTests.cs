using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Signatures;
using PdfSharpCore.Signing;
using PdfSharpCore.Test.Helpers;
using Xunit;
using Reader = PdfSharpCore.Pdf.IO.PdfReader;

namespace PdfSharpCore.Test.IO;

/// <summary>
///   PAdES B-LT / LTV: the certificates and revocation responses that let a signature still be
///   checked once its certificate has expired, written into the document's security store
///   (<c>/DSS</c>) by an incremental update so the signature they vouch for is not disturbed.
///   <see cref="StubRevocationDataProvider"/> mints nothing real — nothing here checks what an OCSP
///   response or a CRL actually says, only that the evidence a provider hands back ends up in the
///   file and that adding it leaves the signature it describes alone.
/// </summary>
public class SignatureValidationDataTests
{
    [Fact]
    public void ValidationDataLeavesAFreshSignatureIntact()
    {
        var signed = Sign(Unsigned());

        var withData = AddValidationData(signed, new StubRevocationDataProvider());

        // Intact, not IsValid: adding validation data appends a revision after the signature, exactly
        // as signing a document twice does, and PdfSignatureVerifier reports that honestly — the
        // signature no longer covers the whole file, because the DSS came after it. What matters here
        // is that the hash the signature carries still verifies over the bytes it always covered.
        var verification = PdfSignatureVerifier.Verify(withData).Single();
        verification.IsIntact.Should().BeTrue();
    }

    [Fact]
    public void ValidationDataAddedToASignatureFromAnotherProducerLeavesItIntact()
    {
        // "Another producer" here is only "signed earlier, by a call that has already returned" —
        // nothing about adding validation data needs the private key that made the signature, only
        // the certificates it already carries, which is what makes archiving someone else's document
        // possible at all.
        var signed = Sign(Unsigned());

        var withData = AddValidationData(signed, new StubRevocationDataProvider());

        PdfSignatureVerifier.Verify(withData).Single().IsIntact.Should().BeTrue();
    }

    [Fact]
    public void AReopenedDocumentWithValidationDataReportsItIsPresent()
    {
        var signed = Sign(Unsigned());
        var withData = AddValidationData(signed, new StubRevocationDataProvider());

        var document = Reader.Open(new MemoryStream(withData), PdfDocumentOpenMode.ReadOnly);

        PdfValidationData.IsPresent(document).Should().BeTrue();
    }

    [Fact]
    public void ADocumentWithNoValidationDataReportsItIsAbsent()
    {
        var document = Reader.Open(new MemoryStream(Sign(Unsigned())), PdfDocumentOpenMode.ReadOnly);

        PdfValidationData.IsPresent(document).Should().BeFalse();
    }

    [Fact]
    public void TheStoreCarriesTheCertificateAndTheEvidenceTheProviderSupplied()
    {
        var provider = new StubRevocationDataProvider();
        var withData = AddValidationData(Sign(Unsigned()), provider);

        var document = Reader.Open(new MemoryStream(withData), PdfDocumentOpenMode.ReadOnly);
        var dss = document.Internals.Catalog.Elements.GetDictionary("/DSS");

        dss.Should().NotBeNull();
        dss.Elements.GetArray("/Certs").Elements.Count.Should().BeGreaterThan(0);
        dss.Elements.GetArray("/OCSPs").Elements.Count.Should().Be(1);
    }

    [Fact]
    public void AddingValidationDataToADocumentNotOpenedForAppendingIsRefused()
    {
        var document = new PdfDocument();
        document.AddPage();

        Action adding = () => PdfValidationData.Add(document, new MemoryStream(),
            new PdfValidationDataEntry(Array.Empty<byte[]>(), Array.Empty<byte[]>(), Array.Empty<byte[]>()));

        adding.Should().Throw<InvalidOperationException>().WithMessage("*Append*");
    }

    /// <summary>
    ///   Hands back fixed bytes rather than a real OCSP response — "responses it minted itself", in
    ///   the spec's words. Nothing downstream checks OCSP semantics, only that whatever a provider
    ///   returns is what ends up stored, so a real response would test nothing this doesn't.
    /// </summary>
    sealed class StubRevocationDataProvider : IRevocationDataProvider
    {
        public RevocationData GetRevocationData(X509Certificate2 certificate, X509Certificate2Collection chain) =>
            new(new[] { new byte[] { 0x30, 0x03, 0x0A, 0x01, 0x00 } }, Array.Empty<byte[]>());
    }

    static byte[] Unsigned()
    {
        var document = new PdfDocument();
        document.AddPage();

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    static byte[] Sign(byte[] document)
    {
        using var input = new MemoryStream(document);
        using var output = new MemoryStream();

        PdfSigner.Sign(input, output, new Pkcs7Signer(SigningCertificates.Default));
        return output.ToArray();
    }

    static byte[] AddValidationData(byte[] document, IRevocationDataProvider provider)
    {
        var opened = Reader.Open(new MemoryStream(document), PdfDocumentOpenMode.Append);

        using var output = new MemoryStream();
        PdfSignatureValidationData.Add(opened, output, provider);
        return output.ToArray();
    }
}
