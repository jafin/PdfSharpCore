using System;
using System.IO;
using System.Reflection;

namespace ConformanceCorpus;

/// <summary>
/// The bytes of an sRGB ICC profile, for the output intent every PDF/A document has to carry.
/// </summary>
/// <remarks>
/// <para>
/// <b>A file, and it was not always.</b> No profile ships with PdfSharpCore — the writer embeds the
/// bytes it is given and says so when given none — so a corpus of PDF/A documents has to get one
/// from somewhere, and for a while this class built one: an ICC v2.1 matrix-shaper display profile
/// written out byte by byte, with sRGB's primaries adapted to D50 and a single gamma of 2.2 standing
/// in for sRGB's piecewise transfer curve. That was 250 lines, and the gamma was a documented
/// approximation.
/// </para>
/// <para>
/// It existed because the three obvious alternatives were worse. Downloading a profile during the
/// build makes validation depend on a third party being up. Skia, already a dependency, parses
/// profiles but does not write them — <c>SKColorSpace.ToProfile()</c> hands back a structure whose
/// buffer is the bytes it was parsed from, so for a colour space that was never parsed from anything
/// there are none. And a checked-in <c>.icc</c> is a binary blob with a licence of its own to
/// account for.
/// </para>
/// <para>
/// <b>Only the third of those was ever about licences rather than about tools, and it now has an
/// answer.</b> <c>assets/icc/sRGB-v2-micro.icc</c> is public domain under CC0, needs no attribution,
/// carries the true sRGB primaries and samples the real transfer curve at 42 points rather than
/// approximating it — and is the same file the demonstration app embeds, so what a validator passes
/// here is what a user actually gets. <c>assets/icc/LICENSE.txt</c> records where it came from.
/// </para>
/// <para>
/// Linked from a directory belonging to neither project rather than reached for inside one of them.
/// Both need it equally, and a corpus that gates CI should not fail because a demo app reorganised
/// its assets.
/// </para>
/// </remarks>
static class SrgbProfile
{
    /// <summary>
    /// The name the profile is embedded under, set by <c>LogicalName</c> in the project file.
    /// </summary>
    const string Resource = "ConformanceCorpus.sRGB-v2-micro.icc";

    /// <summary>
    /// Where an ICC profile's own file signature sits, and what it says. Every profile carries
    /// <c>acsp</c> there and nothing else does.
    /// </summary>
    const int SignatureAt = 36;

    internal static byte[] Bytes()
    {
        using var stream = typeof(SrgbProfile).GetTypeInfo().Assembly
            .GetManifestResourceStream(Resource);

        if (stream == null)
            throw new InvalidOperationException(
                "The ICC profile '" + Resource + "' is not embedded in this assembly, so no document "
                + "in this corpus can carry the output intent PDF/A requires of it.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        // Checked because the failure it catches is otherwise a mystery. A binary that has been
        // through line-ending normalisation is still a file, still embeds, and produces a corpus
        // that fails validation on the output intent of every document at once - which reads as a
        // regression in the writer rather than as a mangled asset. Saying so here costs four
        // comparisons per document and names the actual cause.
        if (bytes.Length < SignatureAt + 4
            || bytes[SignatureAt] != 'a' || bytes[SignatureAt + 1] != 'c'
            || bytes[SignatureAt + 2] != 's' || bytes[SignatureAt + 3] != 'p')
        {
            throw new InvalidOperationException(
                "'" + Resource + "' is embedded but is not an ICC profile: the 'acsp' signature every "
                + "profile carries at byte " + SignatureAt + " is not there. The likely cause is the "
                + "file having been treated as text somewhere between the repository and here.");
        }

        return bytes;
    }
}
