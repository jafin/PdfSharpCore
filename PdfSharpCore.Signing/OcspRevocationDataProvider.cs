using System;
using System.Formats.Asn1;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace PdfSharpCore.Signing;

/// <summary>
/// Fetches an OCSP response for a certificate over HTTP, per RFC 6960, using the responder named in
/// the certificate's own Authority Information Access extension.
/// </summary>
/// <remarks>
/// <para>
/// The shipped implementation for real use, the way <see cref="Rfc3161TimestampProvider"/> is for
/// timestamps. It supplies OCSP responses only — CRLs are left empty in what it answers — because OCSP
/// is what a certificate names its own responder for; fetching a CRL from a certificate's distribution
/// point is a caller's to add if it wants both channels covered.
/// </para>
/// <para>
/// A certificate with no OCSP responder named, or whose issuer is not among the certificates handed
/// to <see cref="GetRevocationData"/>, is not a failure: it answers <see cref="RevocationData.None"/>
/// rather than throw, because gathering validation data is inherently best-effort — not every
/// certificate carries evidence, and one that has none is not the request that should fail.
/// </para>
/// </remarks>
public sealed class OcspRevocationDataProvider : IRevocationDataProvider, IDisposable
{
    /// <summary>id-pkix-ocsp, RFC 6960.</summary>
    const string OcspAccessMethodOid = "1.3.6.1.5.5.7.48.1";

    /// <summary>id-pe-authorityInfoAccess, RFC 5280.</summary>
    const string AuthorityInfoAccessOid = "1.3.6.1.5.5.7.1.1";

    readonly HttpClient _httpClient;
    readonly bool _ownsHttpClient;

    /// <param name="httpClient">
    /// Reused rather than created per call, if given. Left unset, this makes and owns one for its own
    /// lifetime — construct one instance and reuse it rather than making one per certificate.
    /// </param>
    public OcspRevocationDataProvider(HttpClient httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }
    }

    /// <inheritdoc/>
    public RevocationData GetRevocationData(X509Certificate2 certificate, X509Certificate2Collection chain)
    {
        if (certificate == null)
            throw new ArgumentNullException(nameof(certificate));

        var responderUri = OcspResponderOf(certificate);
        if (responderUri == null)
            return RevocationData.None;

        var issuer = IssuerOf(certificate, chain);
        if (issuer == null)
            return RevocationData.None;

        var request = BuildRequest(certificate, issuer);

        try
        {
            using var content = new ByteArrayContent(request);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/ocsp-request");

            using var response = _httpClient.PostAsync(responderUri, content).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            var responseBytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return new RevocationData(new[] { responseBytes }, null);
        }
        catch (Exception problem) when (problem is HttpRequestException or TaskCanceledException)
        {
            return RevocationData.None;
        }
    }

    /// <summary>
    /// The certificate in <paramref name="chain"/> that issued <paramref name="certificate"/>, found
    /// by subject/issuer name rather than by verifying a signature — this is evidence-gathering, not
    /// trust, and the same certificate the OCSP request names its issuer by is what a responder needs.
    /// </summary>
    static X509Certificate2 IssuerOf(X509Certificate2 certificate, X509Certificate2Collection chain)
    {
        if (chain == null)
            return null;

        foreach (X509Certificate2 candidate in chain)
        {
            if (candidate.Thumbprint != certificate.Thumbprint
                && String.Equals(candidate.Subject, certificate.Issuer, StringComparison.Ordinal))
                return candidate;
        }

        return null;
    }

    static Uri OcspResponderOf(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions[AuthorityInfoAccessOid];
        if (extension == null)
            return null;

        var reader = new AsnReader(extension.RawData, AsnEncodingRules.BER);
        var accessDescriptions = reader.ReadSequence();

        while (accessDescriptions.HasData)
        {
            var accessDescription = accessDescriptions.ReadSequence();
            var accessMethod = accessDescription.ReadObjectIdentifier();

            var uriTag = new Asn1Tag(TagClass.ContextSpecific, 6);
            if (accessMethod == OcspAccessMethodOid && accessDescription.PeekTag() == uriTag)
            {
                var uri = accessDescription.ReadCharacterString(UniversalTagNumber.IA5String, uriTag);
                if (Uri.TryCreate(uri, UriKind.Absolute, out var responderUri))
                    return responderUri;
            }
        }

        return null;
    }

    /// <summary>
    /// A minimal, unsigned <c>OCSPRequest</c> asking about one certificate: no requestor name, no
    /// extensions, nothing a responder would need a nonce or a signature to answer.
    /// </summary>
    static byte[] BuildRequest(X509Certificate2 certificate, X509Certificate2 issuer)
    {
        var issuerNameHash = SHA1.HashData(issuer.SubjectName.RawData);
        var issuerKeyHash = SHA1.HashData(issuer.PublicKey.EncodedKeyValue.RawData);

        var serialNumberBytes = certificate.GetSerialNumber(); // little-endian
        var serialNumber = new System.Numerics.BigInteger(serialNumberBytes, isUnsigned: true, isBigEndian: false);

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())      // OCSPRequest
        using (writer.PushSequence())      // TBSRequest
        using (writer.PushSequence())      // requestList
        using (writer.PushSequence())      // Request
        using (writer.PushSequence())      // CertID
        {
            using (writer.PushSequence()) // hashAlgorithm: SHA-1, per RFC 6960's own examples
            {
                writer.WriteObjectIdentifier("1.3.14.3.2.26");
                writer.WriteNull();
            }

            writer.WriteOctetString(issuerNameHash);
            writer.WriteOctetString(issuerKeyHash);
            writer.WriteInteger(serialNumber);
        }

        return writer.Encode();
    }

    /// <summary>
    /// Releases the <see cref="HttpClient"/> this created, if it made one for itself rather than
    /// being handed one to reuse.
    /// </summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
