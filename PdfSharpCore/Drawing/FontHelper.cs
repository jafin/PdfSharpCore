#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PdfSharp.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Diagnostics;
using PdfSharpCore.Fonts;
using PdfSharpCore.Fonts.OpenType;

namespace PdfSharpCore.Drawing;

/// <summary>
/// Bunch of functions that do not have a better place.
/// </summary>
static class FontHelper
{
    /// <summary>
    /// Measure string directly from font data.
    /// </summary>
    public static XSize MeasureString(string text, XFont font, XStringFormat stringFormat)
    {
        XSize size = new XSize();

        OpenTypeDescriptor descriptor = FontDescriptorCache.GetOrCreateDescriptorFor(font) as OpenTypeDescriptor;
        if (descriptor != null)
        {
            // Height is the sum of ascender and descender.
            var singleLineHeight = (descriptor.Ascender + descriptor.Descender) * font.Size / font.UnitsPerEm;
            var lineGapHeight = (descriptor.LineSpacing - descriptor.Ascender - descriptor.Descender) * font.Size / font.UnitsPerEm;

            Debug.Assert(descriptor.Ascender > 0);

            XStringFormat format = stringFormat ?? XStringFormats.Default;

            // Bold simulation strokes the glyphs and widens them by a character spacing of its own
            // (see PdfGraphicsState.RealizeFont), so it counts the same way CharacterSpacing does.
            // Unsure how to deal with white space. Currently count as regular character.
            double boldSimulation =
                (font.GlyphTypeface.StyleSimulations & XStyleSimulations.BoldSimulation) == XStyleSimulations.BoldSimulation
                    ? font.Size * Const.BoldEmphasis
                    : 0;
            double characterSpacing = format.CharacterSpacing + boldSimulation;
            double wordSpacing = format.WordSpacing;

            // A glyph advances by its own width plus the character spacing, and a space by the word
            // spacing on top of that; the horizontal scaling then applies to the lot. PDF 32000-1
            // section 9.4.4.
            double LineWidth(int fontUnits, int glyphs, int spaces)
                => fontUnits * font.Size / descriptor.UnitsPerEm + glyphs * characterSpacing + spaces * wordSpacing;

            bool symbol = descriptor.FontFace.cmap.symbol;
            int length = text.Length;
            var height = singleLineHeight;
            double maxWidth = 0;
            int width = 0;
            int glyphCount = 0;
            int spaceCount = 0;
            for (int idx = 0; idx < length; idx++)
            {
                char ch = text[idx];

                // Handle line feed ( \n)
                if (ch == 10)
                {
                    if (idx < (length - 1))
                    {
                        maxWidth = Math.Max(maxWidth, LineWidth(width, glyphCount, spaceCount));
                        width = 0;
                        glyphCount = 0;
                        spaceCount = 0;
                        height += lineGapHeight + singleLineHeight;
                    }

                    continue;
                }

                // HACK: Handle tabulator sign as space (\t)
                if (ch == 9)
                {
                    ch = ' ';
                }

                // HACK: Unclear what to do here.
                if (ch < 32)
                {
                    continue;
                }

                // Counted before the symbol remapping below, which would turn the space into some
                // other code point entirely.
                if (ch == ' ')
                    spaceCount++;

                if (symbol)
                {
                    // Remap ch for symbol fonts.
                    ch = (char)(ch | (descriptor.FontFace.os2.usFirstCharIndex & 0xFF00));  // @@@ refactor
                    // Used | instead of + because of: http://PdfSharpCore.codeplex.com/workitem/15954
                }
                int glyphIndex = descriptor.CharCodeToGlyphIndex(ch);
                width += descriptor.GlyphIndexToWidth(glyphIndex);
                glyphCount++;
            }
            maxWidth = Math.Max(maxWidth, LineWidth(width, glyphCount, spaceCount));

            // What? size.Width = maxWidth * font.Size * (font.Italic ? 1 : 1) / descriptor.UnitsPerEm;
            size.Width = maxWidth * format.HorizontalScaling / 100;
            size.Height = height;
        }
        Debug.Assert(descriptor != null, "No OpenTypeDescriptor.");

        return size;
    }

    /// <summary>
    /// Calculates an Adler32 checksum combined with the buffer length
    /// in a 64 bit unsigned integer.
    /// </summary>
    public static ulong CalcChecksum(byte[] buffer)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        const uint prime = 65521; // largest prime smaller than 65536
        uint s1 = 0;
        uint s2 = 0;
        int length = buffer.Length;
        int offset = 0;
        while (length > 0)
        {
            int n = 3800;
            if (n > length)
                n = length;
            length -= n;
            while (--n >= 0)
            {
                s1 += buffer[offset++];
                s2 = s2 + s1;
            }
            s1 %= prime;
            s2 %= prime;
        }
        ulong ul1 = (ulong)s2 << 16;
        ul1 = ul1 | s1;
        ulong ul2 = (ulong)buffer.Length;
        return (ul1 << 32) | ul2;
    }

    public static XFontStyle CreateStyle(bool isBold, bool isItalic)
    {
        return (isBold ? XFontStyle.Bold : 0) | (isItalic ? XFontStyle.Italic : 0);
    }
}
