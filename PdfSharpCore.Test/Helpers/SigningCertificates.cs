using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PdfSharpCore.Test.Helpers;

/// <summary>
///   A self-signed certificate to sign test documents with.
/// </summary>
/// <remarks>
///   Generated rather than checked in, because a checked-in certificate expires and then the build
///   fails one morning for a reason nobody changed. Nothing here is trusted by anything — the point
///   of the signing tests is that a signature is intact and covers what it claims to, which is a
///   question about bytes, not about who is believed.
/// </remarks>
public static class SigningCertificates
{
    static readonly Lazy<X509Certificate2> Generated = new(() => Create("CN=PdfSharpCore Test Signer"));

    /// <summary>
    ///   The certificate the signing tests share. Generating an RSA key costs enough to be worth
    ///   doing once.
    /// </summary>
    public static X509Certificate2 Default => Generated.Value;

    public static X509Certificate2 Create(string subject)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        // Round-tripped through PKCS#12 on purpose. A certificate straight out of CreateSelfSigned
        // carries an ephemeral key on Windows, and CMS signing goes looking for that key in a store
        // it was never put in — "Keyset does not exist", from a certificate that plainly has one.
        const string password = "pdfsharpcore";
        var exported = certificate.Export(X509ContentType.Pfx, password);

#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(exported, password, X509KeyStorageFlags.Exportable);
#else
        return new X509Certificate2(exported, password, X509KeyStorageFlags.Exportable);
#endif
    }
}
