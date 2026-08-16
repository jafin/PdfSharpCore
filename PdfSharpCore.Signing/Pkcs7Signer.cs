using System;
using System.Formats.Asn1;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using PdfSharpCore.Pdf.Signatures;

namespace PdfSharpCore.Signing;

/// <summary>
/// Signs with an X.509 certificate, producing the detached CMS signature a PDF signature dictionary
/// holds.
/// </summary>
/// <remarks>
/// <para>
/// Built on <c>System.Security.Cryptography.Pkcs</c>, which is part of the framework on every target
/// this package has — so signing adds no third-party dependency, and the private key can live
/// wherever the platform lets a certificate's key live, including a smart card or an HSM, without
/// this class knowing anything about it.
/// </para>
/// <para>
/// <b>What this produces is a signature, not a trusted one.</b> Whether a reader shows a green tick
/// depends on the certificate chaining to a root it trusts, which is a matter of what certificate is
/// passed in, not of anything here.
/// </para>
/// </remarks>
public sealed class Pkcs7Signer : IPdfSigner
{
    /// <summary>id-aa-signingCertificateV2, RFC 5035.</summary>
    const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";

    readonly X509Certificate2 _certificate;
    readonly X509Certificate2Collection _chain;
    readonly HashAlgorithmName _hashAlgorithm;

    /// <summary>
    /// Signs with the given certificate, which must have a usable private key.
    /// </summary>
    /// <param name="certificate">The signing certificate, with its private key.</param>
    /// <param name="format">Which flavour of signature to produce. PAdES by default.</param>
    /// <param name="hashAlgorithm">
    /// The digest to sign. SHA-256 by default, which is what everything accepts; SHA-1 is refused.
    /// </param>
    /// <param name="chain">
    /// Intermediate certificates to embed alongside the signing one, so a verifier that does not
    /// already hold them can still build the chain. The signing certificate is always embedded.
    /// </param>
    public Pkcs7Signer(X509Certificate2 certificate, PdfSignatureFormat format = PdfSignatureFormat.Pades,
        HashAlgorithmName? hashAlgorithm = null, X509Certificate2Collection chain = null)
    {
        _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));

        if (!certificate.HasPrivateKey)
            throw new ArgumentException(
                "The certificate has no private key, so it can identify a signer but cannot be one.",
                nameof(certificate));

        _hashAlgorithm = hashAlgorithm ?? HashAlgorithmName.SHA256;
        if (_hashAlgorithm == HashAlgorithmName.MD5 || _hashAlgorithm == HashAlgorithmName.SHA1)
            throw new ArgumentException(
                "SHA-1 and MD5 are broken for signatures and readers reject them. Use SHA-256 or better.",
                nameof(hashAlgorithm));

        Format = format;
        _chain = chain;
        IncludeSigningTime = format == PdfSignatureFormat.Pkcs7;
    }

    /// <summary>
    /// Which flavour of signature this produces.
    /// </summary>
    public PdfSignatureFormat Format { get; }

    /// <inheritdoc/>
    public string SubFilter =>
        Format == PdfSignatureFormat.Pades ? "/ETSI.CAdES.detached" : "/adbe.pkcs7.detached";

    /// <summary>
    /// How much room to reserve in the file for the signature.
    /// </summary>
    /// <remarks>
    /// The default is generous on purpose. A CMS signature with one RSA-2048 certificate is around
    /// 1.5 kB, but the embedded chain, the key size and the algorithm all move it, and the cost of
    /// guessing high is a few kilobytes of file while the cost of guessing low is an exception after
    /// the document has already been written.
    /// </remarks>
    public int EstimatedSignatureSize { get; set; } = 16384;

    /// <summary>
    /// Whether to record the signing time as a signed CMS attribute as well as in the signature
    /// dictionary's <c>/M</c>.
    /// </summary>
    /// <remarks>
    /// <b>Off for PAdES and on for PKCS#7</b>, which is what the two formats ask for. PAdES carries
    /// the claimed signing time in <c>/M</c> and the ETSI profiles have said since TS 102 778 that
    /// the CMS <c>signing-time</c> attribute should not be there as well — two claimed times that
    /// can disagree help nobody. Either way the time is the signer's own and proves nothing; only a
    /// timestamp from a time-stamping authority does, and that is PAdES B-T.
    /// </remarks>
    public bool IncludeSigningTime { get; set; }

    /// <inheritdoc/>
    public byte[] Sign(Stream content)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        var signed = new SignedCms(new ContentInfo(ReadAll(content)), detached: true);

        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, _certificate)
        {
            DigestAlgorithm = new Oid(OidOf(_hashAlgorithm)),
            IncludeOption = X509IncludeOption.WholeChain
        };

        if (_chain != null)
        {
            foreach (var certificate in _chain)
                signer.Certificates.Add(certificate);
        }

        if (IncludeSigningTime)
            signer.SignedAttributes.Add(new Pkcs9SigningTime(GlobalTimeSettings.Now));

        if (Format == PdfSignatureFormat.Pades)
            signer.SignedAttributes.Add(SigningCertificateV2());

        signed.ComputeSignature(signer);
        return signed.Encode();
    }

    /// <summary>
    /// The signing-certificate-v2 signed attribute: a hash of the signing certificate, inside what
    /// is signed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole difference between a PKCS#7 signature and a CAdES one, and it closes a real
    /// hole. Without it, the certificate is merely carried alongside the signature, and an attacker
    /// who can find a second certificate whose key verifies the same signature can swap it in and
    /// change who the document appears to have been signed by. Hashing the certificate into the
    /// signed attributes makes the pairing part of what was signed.
    /// </para>
    /// <para>
    /// <c>ESSCertIDv2</c> (RFC 5035) defaults its hash algorithm to SHA-256, so for SHA-256 the
    /// algorithm identifier is left out — a DER encoder must omit a field that equals its default,
    /// and a verifier reading it back supplies SHA-256. The issuer and serial are optional and are
    /// left out too; the certificate hash alone is what does the binding.
    /// </para>
    /// </remarks>
    AsnEncodedData SigningCertificateV2()
    {
        var hash = _certificate.GetCertHash(_hashAlgorithm);

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())        // SigningCertificateV2
        using (writer.PushSequence())        // certs SEQUENCE OF ESSCertIDv2
        using (writer.PushSequence())        // ESSCertIDv2
        {
            if (_hashAlgorithm != HashAlgorithmName.SHA256)
            {
                using (writer.PushSequence())
                    writer.WriteObjectIdentifier(OidOf(_hashAlgorithm));
            }

            writer.WriteOctetString(hash);
        }

        return new AsnEncodedData(new Oid(SigningCertificateV2Oid), writer.Encode());
    }

    static string OidOf(HashAlgorithmName algorithm)
    {
        if (algorithm == HashAlgorithmName.SHA256)
            return "2.16.840.1.101.3.4.2.1";
        if (algorithm == HashAlgorithmName.SHA384)
            return "2.16.840.1.101.3.4.2.2";
        if (algorithm == HashAlgorithmName.SHA512)
            return "2.16.840.1.101.3.4.2.3";

        throw new ArgumentException(
            "Only SHA-256, SHA-384 and SHA-512 are supported for signing.", nameof(algorithm));
    }

    static byte[] ReadAll(Stream stream)
    {
        if (stream is MemoryStream already)
            return already.ToArray();

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
