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
using System.Globalization;
using PdfSharpCore.Pdf.IO;

namespace PdfSharpCore.Pdf
{
    /// <summary>
    /// Represents a direct date value.
    /// </summary>
    [DebuggerDisplay("({Value})")]
    public sealed class PdfDate : PdfItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PdfDate"/> class. A string that cannot be read
        /// as a date gives DateTime.MinValue. Use <see cref="TryParse"/> to tell the two apart.
        /// </summary>
        public PdfDate(string value)
        {
            DateTimeOffset parsed;
            _value = TryParse(value, out parsed) ? parsed : new DateTimeOffset(DateTime.MinValue.Ticks, TimeSpan.Zero);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfDate"/> class. A value of unspecified kind is
        /// taken to be local time, which is what a date without a stated offset means to the machine
        /// that writes it.
        /// </summary>
        public PdfDate(DateTime value)
        {
            _value = WithLocalOffset(value);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfDate"/> class.
        /// </summary>
        public PdfDate(DateTimeOffset value)
        {
            _value = value;
        }

        /// <summary>
        /// Gets the value as DateTime, in Universal Time. A PDF date is local time and an offset, and a
        /// DateTime cannot hold the offset; <see cref="ValueOffset"/> can, and is what a caller that
        /// wants back the value it wrote should use.
        /// </summary>
        public DateTime Value =>
            // This class must behave like a value type. Therefore it cannot be changed (like System.String).
            _value.UtcDateTime;

        /// <summary>
        /// Gets the value as DateTimeOffset: the local time the document states, together with its
        /// offset from Universal Time.
        /// </summary>
        public DateTimeOffset ValueOffset => _value;

        readonly DateTimeOffset _value;

        /// <summary>
        /// Returns the value in the PDF date format.
        /// </summary>
        public override string ToString()
        {
            // The trailing apostrophe belongs to PDF 1.7 and was dropped by PDF 2.0, which is why the
            // parser takes it or leaves it. It is written because Acrobat reads an offset without it by
            // rounding down to the whole hour, which loses the half hour zones.
            TimeSpan offset = _value.Offset;
            char sign = offset < TimeSpan.Zero ? '-' : '+';
            return $"D:{_value.DateTime:yyyyMMddHHmmss}{sign}{Math.Abs(offset.Hours):00}'{Math.Abs(offset.Minutes):00}'";
        }

        /// <summary>
        /// Reads a PDF date string. Everything after the year may be left out, and what is left out
        /// takes the value the standard gives it: 01 for the month and the day, zero for the rest, and
        /// Universal Time for an unstated offset.
        /// </summary>
        /// <returns>True if the string could be read as a date.</returns>
        public static bool TryParse(string date, out DateTimeOffset value)
        {
            value = default(DateTimeOffset);
            if (String.IsNullOrEmpty(date))
                return false;

            // "The prefix D:, although also optional, is strongly recommended."
            int index = date.StartsWith("D:", StringComparison.Ordinal) ? 2 : 0;

            int year;
            if (!TryReadDigits(date, ref index, 4, out year))
                return TryReadPlainEnglish(date, out value);

            // Reading stops at the first field that is not there, or is not digits. The two cannot be
            // told apart without rules the standard does not give, and stopping is what a date that
            // simply ends looks like.
            int month, day, hour, minute, second;
            if (!TryReadDigits(date, ref index, 2, out month))
                month = 1;
            if (!TryReadDigits(date, ref index, 2, out day))
                day = 1;
            if (!TryReadDigits(date, ref index, 2, out hour))
                hour = 0;
            if (!TryReadDigits(date, ref index, 2, out minute))
                minute = 0;
            if (!TryReadDigits(date, ref index, 2, out second))
                second = 0;

            // There are miserable PDF tools around the world.
            if (year < 1 || year > 9999)
                return false;
            month = Math.Min(Math.Max(month, 1), 12);
            day = Math.Min(Math.Max(day, 1), DateTime.DaysInMonth(year, month));

            TimeSpan offset;
            if (!TryReadOffset(date, index, out offset))
                return false;

            try
            {
                value = new DateTimeOffset(new DateTime(year, month, day, hour, minute, second), offset);
                return true;
            }
            catch (ArgumentException)
            {
                // An hour, a minute, a second or an offset outside what a date can hold.
                return false;
            }
        }

        /// <summary>
        /// Reads the relationship of the stated time to Universal Time. The offset is written as a sign
        /// and then hours and minutes, and the apostrophes between and after them are there in some
        /// documents and not in others, so they are skipped wherever they fall.
        /// </summary>
        static bool TryReadOffset(string date, int index, out TimeSpan offset)
        {
            offset = TimeSpan.Zero;

            // "If no UT information is specified, the relationship of the specified time to UT shall be
            // considered to be GMT." A Z says the same thing outright.
            if (index >= date.Length || date[index] == 'Z')
                return true;

            char sign = date[index];
            if (sign != '+' && sign != '-')
                return true;    // Trailing rubbish, of which there is plenty. The date itself stands.
            index++;

            int hours, minutes;
            if (!TryReadDigits(date, ref index, 2, out hours))
                return true;
            SkipApostrophe(date, ref index);
            if (!TryReadDigits(date, ref index, 2, out minutes))
                minutes = 0;

            if (hours > 23 || minutes > 59)
                return false;

            offset = new TimeSpan(hours, minutes, 0);
            if (sign == '-')
                offset = offset.Negate();
            return true;
        }

        /// <summary>
        /// Some libraries write a date the way a person would say it. Such a date states no offset, so
        /// it is read as local time, which is the only guess to be had.
        /// </summary>
        static bool TryReadPlainEnglish(string date, out DateTimeOffset value)
        {
            DateTime parsed;
            if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                value = WithLocalOffset(parsed);
                return true;
            }

            value = default(DateTimeOffset);
            return false;
        }

        static bool TryReadDigits(string date, ref int index, int count, out int value)
        {
            value = 0;
            if (index + count > date.Length)
                return false;

            int result = 0;
            for (int idx = index; idx < index + count; idx++)
            {
                if (date[idx] < '0' || date[idx] > '9')
                    return false;
                result = result * 10 + (date[idx] - '0');
            }

            index += count;
            value = result;
            return true;
        }

        static void SkipApostrophe(string date, ref int index)
        {
            if (index < date.Length && date[index] == '\'')
                index++;
        }

        /// <summary>
        /// Gives a DateTime the offset it is understood to have. A date at the very edge of what a
        /// DateTime can hold cannot carry one, and is recorded as it stands rather than throwing.
        /// </summary>
        static DateTimeOffset WithLocalOffset(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
                return new DateTimeOffset(value);

            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(value);
            long utcTicks = value.Ticks - offset.Ticks;
            if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
                return new DateTimeOffset(value.Ticks, TimeSpan.Zero);

            return new DateTimeOffset(value.Ticks, offset);
        }

        /// <summary>
        /// Writes the value in the PDF date format.
        /// </summary>
        internal override void WriteObject(PdfWriter writer)
        {
            writer.WriteDocString(ToString());
        }
    }
}
