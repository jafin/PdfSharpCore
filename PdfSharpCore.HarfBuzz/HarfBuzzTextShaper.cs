using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using HarfBuzzSharp;
using PdfSharpCore.Fonts;
using Buffer = HarfBuzzSharp.Buffer;

namespace PdfSharpCore.HarfBuzz;

/// <summary>
/// Shapes text with HarfBuzz: runs the face's own <c>GSUB</c> and <c>GPOS</c> tables, so that
/// Arabic letters join, Devanagari reorders, and Latin kerns and ligates.
/// </summary>
/// <remarks>
/// <para>
/// Register it once, before anything draws:
/// <code>GlobalFontSettings.TextShaper = new HarfBuzzTextShaper();</code>
/// </para>
/// <para>
/// This is a package of its own rather than a class in <c>PdfSharpCore.Skia</c> because shaping
/// must not require a particular imaging backend. It depends on <c>HarfBuzzSharp</c> alone, so a
/// consumer using <c>PdfSharpCore.ImageSharp</c> - which is pinned to SixLabors.Fonts 1.0.1 for
/// licence reasons and cannot shape for itself - gets the same shaping a Skia consumer does.
/// </para>
/// <para>
/// Faces are parsed once and kept, keyed on <see cref="ShapingFont.Key"/>, because parsing a font
/// per word would cost more than shaping it. Shaping against one face is serialised: a HarfBuzz
/// font is not safe to shape with from two threads at once, and two different faces still shape
/// concurrently.
/// </para>
/// </remarks>
public sealed class HarfBuzzTextShaper : ITextShaper, IDisposable
{
    // Lazy rather than the face itself, because ConcurrentDictionary.GetOrAdd may run its factory
    // on several threads at once and keep only one of the results. Every other thread's face is
    // then abandoned holding live native handles, to be freed by a finaliser at some later moment
    // while the survivor is in use. That is not a leak that stays quiet: it showed up as a run
    // coming back unkerned every few hundred shapings. ExecutionAndPublication means the face is
    // built exactly once however many threads arrive together.
    readonly ConcurrentDictionary<string, Lazy<ShapedFace>> _faces =
        new ConcurrentDictionary<string, Lazy<ShapedFace>>();

    bool _disposed;

    /// <inheritdoc/>
    public ShapedRun Shape(ReadOnlySpan<char> text, ShapingFont font, XTextDirection direction,
        string script, string language)
    {
        if (font == null)
            throw new ArgumentNullException(nameof(font));

        if (_disposed)
            throw new ObjectDisposedException(nameof(HarfBuzzTextShaper));

        if (text.Length == 0)
            return ShapedRun.Empty(font.UnitsPerEm, direction);

        var face = _faces.GetOrAdd(
            font.Key ?? font.FaceName ?? string.Empty,
            _ => new Lazy<ShapedFace>(() => new ShapedFace(font),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        // A face this shaper could not parse is declined rather than drawn wrong: returning null
        // puts the caller back on the one-character-one-glyph path, which is what it would have
        // done had no shaper been registered at all.
        if (face.Failed)
            return null;

        return face.Shape(text, direction, script, language);
    }

    /// <summary>
    /// Releases every face parsed so far. The shaper is unusable afterwards; a document being
    /// drawn is still holding on to nothing of it, because a <see cref="ShapedRun"/> is managed
    /// memory and keeps no native handle.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var face in _faces.Values)
        {
            // A face nothing ever asked for was never built, and building it here only to free it
            // would be a strange way to spend the time.
            if (face.IsValueCreated)
                face.Value.Dispose();
        }

        _faces.Clear();
    }

    /// <summary>
    /// One parsed face and the HarfBuzz font over it, with the gate that keeps two threads from
    /// shaping with it at once.
    /// </summary>
    sealed class ShapedFace : IDisposable
    {
        readonly object _gate = new object();
        readonly int _unitsPerEm;
        readonly Blob _blob;
        readonly Face _face;
        readonly Font _font;

        internal ShapedFace(ShapingFont font)
        {
            _unitsPerEm = font.UnitsPerEm;

            try
            {
                _blob = BlobOf(font.Bytes);
                _face = new Face(_blob, 0);

                _font = new Font(_face);

                // Scaled to the em, so that HarfBuzz answers in the face's own design units -
                // which is what ShapedGlyph.Advance is defined in, and what lets one shaped run be
                // measured and drawn at any size.
                _font.SetScale(_unitsPerEm, _unitsPerEm);
                _font.SetFunctionsOpenType();
            }
            catch (Exception)
            {
                // A file HarfBuzz will not parse is not a reason to bring a page down. Whatever
                // was built gets released and the shaper declines this face from here on.
                Dispose();
                Failed = true;
            }
        }

        internal bool Failed { get; }

        /// <summary>
        /// The font file in memory the blob owns outright, freed when the blob is.
        /// </summary>
        /// <remarks>
        /// Unmanaged memory and an explicit release delegate rather than <c>Blob.FromStream</c>,
        /// because a HarfBuzz face reads its tables lazily and goes on reading them for as long as
        /// it lives. Whatever the managed bytes were - an array the caller is about to drop, a
        /// slice of a larger buffer, a stream that has been disposed - they are not guaranteed to
        /// stay where they were put, and a face reading a moved array is a face that shapes
        /// correctly nearly every time. That is how this arrived: a run coming back unkerned once
        /// in a few hundred, with the right glyphs, because the cmap had been read before the
        /// bytes went and GPOS after.
        /// </remarks>
        static Blob BlobOf(ReadOnlyMemory<byte> bytes)
        {
            var array = MemoryMarshal.TryGetArray(bytes, out var segment) && segment.Array != null
                ? segment
                : new ArraySegment<byte>(bytes.ToArray());

            var pointer = Marshal.AllocHGlobal(array.Count);
            try
            {
                Marshal.Copy(array.Array, array.Offset, pointer, array.Count);
                var blob = new Blob(pointer, array.Count, MemoryMode.ReadOnly,
                    () => Marshal.FreeHGlobal(pointer));
                blob.MakeImmutable();
                return blob;
            }
            catch
            {
                Marshal.FreeHGlobal(pointer);
                throw;
            }
        }

        internal ShapedRun Shape(ReadOnlySpan<char> text, XTextDirection direction,
            string script, string language)
        {
            lock (_gate)
            {
                using var buffer = new Buffer();
                buffer.AddUtf16(text);
                buffer.Direction = direction == XTextDirection.RightToLeft
                    ? Direction.RightToLeft
                    : Direction.LeftToRight;

                if (TryParseScript(script, out var parsed))
                    buffer.Script = parsed;

                if (!string.IsNullOrEmpty(language))
                    buffer.Language = new Language(language);

                // Fills in whatever of direction, script and language was left unsaid, from the
                // characters themselves. It never overwrites what was set above.
                buffer.GuessSegmentProperties();

                _font.Shape(buffer);

                var infos = buffer.GlyphInfos;
                var positions = buffer.GlyphPositions;
                var glyphs = new ShapedGlyph[infos.Length];

                for (int idx = 0; idx < infos.Length; idx++)
                {
                    var position = positions[idx];
                    glyphs[idx] = new ShapedGlyph(
                        (ushort)infos[idx].Codepoint,
                        (int)infos[idx].Cluster,
                        position.XAdvance,
                        position.XOffset,
                        position.YOffset);
                }

                return new ShapedRun(glyphs, _unitsPerEm, direction);
            }
        }

        /// <summary>
        /// An ISO 15924 tag as HarfBuzz understands it. A tag is four characters; anything else is
        /// left for <c>GuessSegmentProperties</c> to work out from the text.
        /// </summary>
        static bool TryParseScript(string script, out Script parsed)
        {
            parsed = default;
            if (string.IsNullOrEmpty(script) || script.Length != 4)
                return false;

            parsed = Script.Parse(script);
            return true;
        }

        public void Dispose()
        {
            _font?.Dispose();
            _face?.Dispose();
            _blob?.Dispose();
        }
    }
}
