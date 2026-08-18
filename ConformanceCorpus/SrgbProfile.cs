using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ConformanceCorpus;

/// <summary>
/// The bytes of an sRGB ICC profile, for the output intent every PDF/A document has to carry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built here rather than checked in or asked of a library.</b> No profile ships with
/// PdfSharpCore — the writer embeds the bytes it is given and says so when given none — so a corpus
/// of PDF/A documents has to get one from somewhere, and the three obvious somewheres are all worse
/// than this. A checked-in <c>.icc</c> is a binary blob in the repository with a licence of its own
/// to account for. Downloading one during the build makes the validation step depend on a third
/// party being up. And Skia, which is already a dependency, parses profiles but does not write them:
/// <c>SKColorSpace.ToProfile()</c> hands back a structure whose buffer is the bytes it was parsed
/// from, so for a colour space that was never parsed from anything there are no bytes at all.
/// </para>
/// <para>
/// It has to be a <em>real</em> profile, which is the trap. The unit tests covering the XMP writer
/// pass the ASCII bytes <c>NOT-AN-ICC-PROFILE</c>, and they are right to: nothing in this library
/// parses a profile, so a legible stand-in makes those assertions clearer and says plainly that they
/// are not colour-management tests. A validator does parse it — it reads the colour space out of the
/// header and checks the output intent agrees — so the stand-in that serves the unit tests would fail
/// every document in this corpus for a reason having nothing to do with the code under test.
/// </para>
/// <para>
/// What is built is the smallest thing that is genuinely an sRGB profile: an ICC v2.1 matrix-shaper
/// display profile, with the primaries and white point of sRGB already adapted to the D50 profile
/// connection space, and a single gamma of 2.2 standing in for sRGB's piecewise transfer curve. That
/// last one is an approximation, and a deliberate one — the curve matters to colour management and
/// this profile is never used to manage any colour. It exists so the document can name the space its
/// numbers are in, which is what PDF/A asks for.
/// </para>
/// </remarks>
static class SrgbProfile
{
    internal static byte[] Bytes()
    {
        // The order is the order they are written. ICC does not require the table to be sorted, and
        // grouping them by what they say reads better than sorting by signature would.
        var tags = new List<(string Signature, byte[] Data)>
        {
            ("desc", Description("sRGB IEC61966-2.1")),
            ("cprt", Text("No copyright, generated for validation")),

            // D50, because that is the profile connection space every ICC profile is expressed in.
            ("wtpt", Xyz(0.9642, 1.0000, 0.8249)),

            // sRGB's primaries, Bradford-adapted from D65 to D50. These are the values the reference
            // sRGB profiles carry; deriving them here would be arithmetic nobody could check.
            ("rXYZ", Xyz(0.4360, 0.2225, 0.0139)),
            ("gXYZ", Xyz(0.3851, 0.7169, 0.0971)),
            ("bXYZ", Xyz(0.1431, 0.0606, 0.7141)),

            ("rTRC", Gamma(2.2)),
            ("gTRC", Gamma(2.2)),
            ("bTRC", Gamma(2.2)),
        };

        // Header, then the table, then the data each tag points into. Every tag has to begin on a
        // four-byte boundary, so the offsets are worked out before anything is written.
        var tableSize = 4 + 12 * tags.Count;
        var offsets = new int[tags.Count];
        var at = 128 + tableSize;
        for (var index = 0; index < tags.Count; index++)
        {
            at = Aligned(at);
            offsets[index] = at;
            at += tags[index].Data.Length;
        }

        var total = Aligned(at);
        var profile = new MemoryStream(total);

        WriteHeader(profile, total);

        WriteUInt32(profile, (uint)tags.Count);
        for (var index = 0; index < tags.Count; index++)
        {
            WriteSignature(profile, tags[index].Signature);
            WriteUInt32(profile, (uint)offsets[index]);
            WriteUInt32(profile, (uint)tags[index].Data.Length);
        }

        for (var index = 0; index < tags.Count; index++)
        {
            Pad(profile, offsets[index]);
            profile.Write(tags[index].Data, 0, tags[index].Data.Length);
        }

        Pad(profile, total);
        return profile.ToArray();
    }

    // ── The header ──────────────────────────────────────────────────────────────────────────────

    static void WriteHeader(Stream profile, int total)
    {
        WriteUInt32(profile, (uint)total);
        WriteUInt32(profile, 0);                        // no preferred CMM
        WriteUInt32(profile, 0x02100000);               // ICC v2.1
        WriteSignature(profile, "mntr");                // a display profile
        WriteSignature(profile, "RGB ");
        WriteSignature(profile, "XYZ ");                // the connection space

        // A fixed date rather than the time of the run. The corpus is regenerated on every build and
        // a timestamp would make every document differ from the last for no reason anyone cares
        // about, which is a poor thing to hand somebody comparing two validation reports.
        WriteUInt16(profile, 2026);
        WriteUInt16(profile, 1);
        WriteUInt16(profile, 1);
        WriteUInt16(profile, 0);
        WriteUInt16(profile, 0);
        WriteUInt16(profile, 0);

        WriteSignature(profile, "acsp");                // says the file is a profile at all
        WriteUInt32(profile, 0);                        // platform: none in particular
        WriteUInt32(profile, 0);                        // flags: not embedded, use anywhere
        WriteUInt32(profile, 0);                        // device manufacturer
        WriteUInt32(profile, 0);                        // device model
        WriteUInt32(profile, 0);                        // device attributes, both words
        WriteUInt32(profile, 0);
        WriteUInt32(profile, 0);                        // rendering intent: perceptual

        // The illuminant of the connection space, which is D50 by definition and is checked as such.
        WriteFixed(profile, 0.9642);
        WriteFixed(profile, 1.0000);
        WriteFixed(profile, 0.8249);

        WriteUInt32(profile, 0);                        // profile creator

        // Profile ID and the reserved tail, zero throughout. The ID is a v4 field and a v2 profile
        // leaves it unset; zero is what "unset" means for both.
        for (var index = 0; index < 44; index++)
            profile.WriteByte(0);
    }

    // ── The tag types this profile needs ────────────────────────────────────────────────────────

    /// <summary>
    /// A <c>textDescriptionType</c>, which is what ICC v2 spends a profile's name on.
    /// </summary>
    /// <remarks>
    /// Ungainly by modern standards and fixed by the standard: an ASCII string, then room for a
    /// Unicode translation, then a 67-byte Macintosh ScriptCode field that has been vestigial for
    /// decades. The two translations are left empty, which is legal and usual.
    /// </remarks>
    static byte[] Description(string name)
    {
        var ascii = Encoding.ASCII.GetBytes(name);
        var tag = new MemoryStream();

        WriteSignature(tag, "desc");
        WriteUInt32(tag, 0);
        WriteUInt32(tag, (uint)ascii.Length + 1);
        tag.Write(ascii, 0, ascii.Length);
        tag.WriteByte(0);

        WriteUInt32(tag, 0);                            // Unicode language code
        WriteUInt32(tag, 0);                            // Unicode count
        WriteUInt16(tag, 0);                            // ScriptCode code
        tag.WriteByte(0);                               // ScriptCode count
        for (var index = 0; index < 67; index++)        // ScriptCode text
            tag.WriteByte(0);

        return tag.ToArray();
    }

    static byte[] Text(string value)
    {
        var ascii = Encoding.ASCII.GetBytes(value);
        var tag = new MemoryStream();

        WriteSignature(tag, "text");
        WriteUInt32(tag, 0);
        tag.Write(ascii, 0, ascii.Length);
        tag.WriteByte(0);

        return tag.ToArray();
    }

    static byte[] Xyz(double x, double y, double z)
    {
        var tag = new MemoryStream();

        WriteSignature(tag, "XYZ ");
        WriteUInt32(tag, 0);
        WriteFixed(tag, x);
        WriteFixed(tag, y);
        WriteFixed(tag, z);

        return tag.ToArray();
    }

    /// <summary>
    /// A <c>curveType</c> holding a single gamma value, which is the shortest legal transfer curve.
    /// </summary>
    /// <remarks>
    /// A count of one means "the one value that follows is a gamma", as against a count of zero for
    /// the identity or a count of many for a sampled table. The value is <c>u8Fixed8</c>, so 2.2
    /// lands on 2.19921875 — the nearest the format can say, and closer than the difference between
    /// this and the piecewise curve it stands in for.
    /// </remarks>
    static byte[] Gamma(double gamma)
    {
        var tag = new MemoryStream();

        WriteSignature(tag, "curv");
        WriteUInt32(tag, 0);
        WriteUInt32(tag, 1);
        WriteUInt16(tag, (ushort)Math.Round(gamma * 256.0));

        return tag.ToArray();
    }

    // ── Writing the primitives, all big-endian ──────────────────────────────────────────────────

    static int Aligned(int offset) => (offset + 3) & ~3;

    static void Pad(Stream profile, int upTo)
    {
        while (profile.Position < upTo)
            profile.WriteByte(0);
    }

    static void WriteSignature(Stream profile, string signature)
    {
        var ascii = Encoding.ASCII.GetBytes(signature);
        if (ascii.Length != 4)
            throw new ArgumentException("An ICC signature is four characters.", nameof(signature));

        profile.Write(ascii, 0, 4);
    }

    static void WriteUInt32(Stream profile, uint value)
    {
        profile.WriteByte((byte)(value >> 24));
        profile.WriteByte((byte)(value >> 16));
        profile.WriteByte((byte)(value >> 8));
        profile.WriteByte((byte)value);
    }

    static void WriteUInt16(Stream profile, ushort value)
    {
        profile.WriteByte((byte)(value >> 8));
        profile.WriteByte((byte)value);
    }

    /// <summary>An <c>s15Fixed16Number</c>: the value scaled by 65536, signed.</summary>
    static void WriteFixed(Stream profile, double value)
        => WriteUInt32(profile, unchecked((uint)(int)Math.Round(value * 65536.0)));
}
