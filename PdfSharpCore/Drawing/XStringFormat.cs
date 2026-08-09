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

namespace PdfSharpCore.Drawing;

/// <summary>
/// Represents the text layout information.
/// </summary>
public class XStringFormat
{
    /// <summary>
    /// Initializes a new instance of the <see cref="XStringFormat"/> class.
    /// </summary>
    public XStringFormat()
    {
    }

    //TODO public StringFormat(StringFormat format);
    //public StringFormat(StringFormatFlags options);
    //public StringFormat(StringFormatFlags options, int language);
    //public object Clone();
    //public void Dispose();
    //private void Dispose(bool disposing);
    //protected override void Finalize();
    //public float[] GetTabStops(out float firstTabOffset);
    //public void SetDigitSubstitution(int language, StringDigitSubstitute substitute);
    //public void SetMeasurableCharacterRanges(CharacterRange[] ranges);
    //public void SetTabStops(float firstTabOffset, float[] tabStops);
    //public override string ToString();

    /// <summary>
    /// Gets or sets horizontal text alignment information.
    /// </summary>
    public XStringAlignment Alignment
    {
        get => _alignment;
        set => _alignment = value;
    }
    XStringAlignment _alignment;

    //public int DigitSubstitutionLanguage { get; }
    //public StringDigitSubstitute DigitSubstitutionMethod { get; }
    //public StringFormatFlags FormatFlags { get; set; }
    //public static StringFormat GenericDefault { get; }
    //public static StringFormat GenericTypographic { get; }
    //public HotkeyPrefix HotkeyPrefix { get; set; }

    /// <summary>
    /// Gets or sets the line alignment.
    /// </summary>
    public XLineAlignment LineAlignment
    {
        get => _lineAlignment;
        set => _lineAlignment = value;
    }
    XLineAlignment _lineAlignment;

    // ----- Text state ---------------------------------------------------------------------------
    //
    // The properties below are the PDF text state parameters of PDF 32000-1 section 9.3. They are
    // carried here rather than on their own type because DrawString and MeasureString already pass
    // an XStringFormat all the way down to the renderer, so nothing else has to change shape to
    // reach it.
    //
    // All of them widen or move a glyph run, so FontHelper.MeasureString has to apply them or
    // measurement and drawing disagree at every line end.

    /// <summary>
    /// Gets or sets the extra space added after each glyph, in points.
    /// Negative values tighten the text. The default is 0.
    /// This is the Tc operand of PDF 32000-1 section 9.3.2.
    /// </summary>
    public double CharacterSpacing
    {
        get => _characterSpacing;
        set => _characterSpacing = value;
    }
    double _characterSpacing;

    /// <summary>
    /// Gets or sets the extra space added after each space character, in points.
    /// Negative values tighten the text. The default is 0.
    /// This is the Tw operand of PDF 32000-1 section 9.3.3, and applies on top of
    /// <see cref="CharacterSpacing"/>, which a space receives as well.
    /// </summary>
    public double WordSpacing
    {
        get => _wordSpacing;
        set => _wordSpacing = value;
    }
    double _wordSpacing;

    /// <summary>
    /// Gets or sets the horizontal scaling of the text, as a percentage. The default is 100.
    /// This is the Tz operand of PDF 32000-1 section 9.3.4, and scales the glyphs, the character
    /// spacing and the word spacing alike.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public double HorizontalScaling
    {
        get => _horizontalScaling;
        set
        {
            // PDF permits any number here, but a run of zero or negative width has no sensible
            // measurement and would break every line-breaking decision taken from it. Mirrored
            // text is XGraphics.ScaleTransform(-1, 1), not a negative scaling.
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "HorizontalScaling must be greater than zero.");
            _horizontalScaling = value;
        }
    }
    double _horizontalScaling = 100;

    /// <summary>
    /// Gets or sets the distance the text is raised above its baseline, in points.
    /// Negative values lower it. The default is 0.
    /// This is the Ts operand of PDF 32000-1 section 9.3.5, and is how superscript and subscript
    /// are expressed. It moves the text without changing how wide it is.
    /// </summary>
    public double TextRise
    {
        get => _textRise;
        set => _textRise = value;
    }
    double _textRise;

    /// <summary>
    /// Gets or sets the angle, in degrees, by which the text is slanted to the right.
    /// Negative values slant it to the left. The default is 0.
    /// This is a skew of the text matrix rather than a text state parameter, and like
    /// <see cref="TextRise"/> it does not change how wide the text is.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The absolute value is 90 or more.</exception>
    public double ObliqueAngle
    {
        get => _obliqueAngle;
        set
        {
            // At 90 degrees the skew is infinite and the glyphs collapse to a line.
            if (value <= -90 || value >= 90)
                throw new ArgumentOutOfRangeException(nameof(value), value, "ObliqueAngle must be greater than -90 and less than 90 degrees.");
            _obliqueAngle = value;
        }
    }
    double _obliqueAngle;

    /// <summary>
    /// Returns true if every text state property still holds its default, and the text can
    /// therefore be drawn without writing any text state operator.
    /// </summary>
    internal bool IsDefaultTextState =>
        _characterSpacing == 0 && _wordSpacing == 0 && _horizontalScaling == 100 &&
        _textRise == 0 && _obliqueAngle == 0;
}
