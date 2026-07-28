using System;
using System.Globalization;
using AwesomeAssertions;
using PdfSharpCore.Pdf;
using Xunit;

namespace PdfSharpCore.Test.Pdfs
{
    public class PdfDateTests
    {
        // format for PDF date is generally D:YYYYMMDDHHmmSSOHH'mm'

        [Fact]
        public void ParseDateString_WithTimezoneOffset()
        {
            var pdfDate = new PdfDate("D:19981223195200-02'00'");
            var expectedDateWithOffset =
                new DateTimeOffset(new DateTime(1998, 12, 23, 19, 52, 0), new TimeSpan(-2, 0, 0));
            pdfDate.Value.ToUniversalTime().Should().Be(expectedDateWithOffset.UtcDateTime);
        }

        [Fact]
        public void ParseDateString_WithNoOffset()
        {
            var pdfDate = new PdfDate("D:19981223195200Z");
            var expectedDateWithOffset =
                new DateTimeOffset(new DateTime(1998, 12, 23, 19, 52, 0), new TimeSpan(0, 0, 0));
            pdfDate.Value.ToUniversalTime().Should().Be(expectedDateWithOffset.UtcDateTime);
        }

        /// <summary>
        ///   The offset is spelled three ways in the wild. PDF 1.7 puts an apostrophe after the hours
        ///   and after the minutes, PDF 2.0 drops the trailing one, and some producers write neither.
        ///   All three say the same thing and used to be read as three different times, because the
        ///   parser found the offset by counting characters rather than by looking for it.
        /// </summary>
        [Theory]
        [InlineData("D:20240601120000+10'00'", "2024-06-01 02:00:00")]     // PDF 1.7
        [InlineData("D:20240601120000+10'00", "2024-06-01 02:00:00")]      // PDF 2.0
        [InlineData("D:20240601120000+1000", "2024-06-01 02:00:00")]       // no apostrophes at all
        [InlineData("D:20240601120000-05'30'", "2024-06-01 17:30:00")]
        [InlineData("D:20240601120000-05'30", "2024-06-01 17:30:00")]
        [InlineData("D:20240601120000-0530", "2024-06-01 17:30:00")]
        [InlineData("D:20240601120000+00'00'", "2024-06-01 12:00:00")]
        [InlineData("D:20240601120000Z", "2024-06-01 12:00:00")]
        public void AnOffsetIsReadHoweverItsApostrophesFall(string date, string expectedUniversalTime)
        {
            var expected = DateTime.SpecifyKind(
                DateTime.Parse(expectedUniversalTime, CultureInfo.InvariantCulture), DateTimeKind.Utc);

            new PdfDate(date).Value.Should().Be(expected);
        }

        /// <summary>
        ///   "All fields after the year are optional", and each one left out takes the value the
        ///   standard gives it: 01 for the month and the day, zero for the rest.
        /// </summary>
        [Theory]
        [InlineData("D:2024", 2024, 1, 1, 0, 0, 0)]
        [InlineData("D:202406", 2024, 6, 1, 0, 0, 0)]
        [InlineData("D:20240601", 2024, 6, 1, 0, 0, 0)]
        [InlineData("D:2024060113", 2024, 6, 1, 13, 0, 0)]
        [InlineData("D:202406011314", 2024, 6, 1, 13, 14, 0)]
        [InlineData("D:20240601131415", 2024, 6, 1, 13, 14, 15)]
        public void AFieldThatIsLeftOutTakesTheValueTheStandardGivesIt(
            string date, int year, int month, int day, int hour, int minute, int second)
        {
            // No offset is stated, and "if no UT information is specified, the relationship of the
            // specified time to UT shall be considered to be GMT".
            new PdfDate(date).ValueOffset
                .Should().Be(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));
        }

        /// <summary>
        ///   "The prefix D:, although also optional, is strongly recommended."
        /// </summary>
        [Fact]
        public void ADateWithoutThePrefixIsStillADate()
        {
            new PdfDate("20240601120000+10'00'").ValueOffset
                .Should().Be(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.FromHours(10)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("D:")]
        [InlineData("D:20xx0601")]
        [InlineData("not a date at all")]
        public void AStringThatIsNoDateIsRefused(string date)
        {
            DateTimeOffset parsed;
            PdfDate.TryParse(date, out parsed).Should().BeFalse();
        }

        /// <summary>
        ///   Some producers write a date the way a person would say it, and PdfSharpCore has always
        ///   taken those too.
        /// </summary>
        [Fact]
        public void ADateWrittenInPlainEnglishIsStillRead()
        {
            new PdfDate("12/23/1998 19:52:00").ValueOffset.DateTime
                .Should().Be(new DateTime(1998, 12, 23, 19, 52, 0));
        }

        /// <summary>
        ///   Acrobat reads an offset with no trailing apostrophe by rounding it down to the whole hour,
        ///   so a half hour zone comes out an hour wrong. PDF 2.0 dropped the apostrophe; keep writing it.
        /// </summary>
        [Theory]
        [InlineData(10, 0, "D:20240601120000+10'00'")]
        [InlineData(-5, -30, "D:20240601120000-05'30'")]
        [InlineData(0, 0, "D:20240601120000+00'00'")]
        [InlineData(5, 45, "D:20240601120000+05'45'")]
        public void AnOffsetIsWrittenWithBothItsApostrophes(int hours, int minutes, string expected)
        {
            var date = new DateTimeOffset(2024, 6, 1, 12, 0, 0, new TimeSpan(hours, minutes, 0));

            new PdfDate(date).ToString().Should().Be(expected);
        }

        /// <summary>
        ///   What is written is the local time the caller gave and the offset that says which local time
        ///   it is, rather than the same instant expressed in UT. A reader showing the date to a person
        ///   shows the hour the document was worked on.
        /// </summary>
        [Fact]
        public void WhatIsWrittenIsLocalTimeAndItsOffsetRatherThanUniversalTime()
        {
            var noonInIndia = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.FromHours(5.5));

            new PdfDate(noonInIndia).ToString().Should().Be("D:20240601120000+05'30'");
        }

        [Theory]
        [InlineData(10, 0)]
        [InlineData(-5, -30)]
        [InlineData(0, 0)]
        [InlineData(13, 45)]
        public void ADateTimeOffsetSurvivesBeingWrittenAndReadBack(int hours, int minutes)
        {
            var date = new DateTimeOffset(2024, 6, 1, 12, 34, 56, new TimeSpan(hours, minutes, 0));

            var readBack = new PdfDate(new PdfDate(date).ToString()).ValueOffset;

            readBack.Should().Be(date);
            readBack.Offset.Should().Be(date.Offset);
        }

        /// <summary>
        ///   A DateTime cannot hold an offset, so what comes back is the instant in Universal Time. That
        ///   is the same moment, spelled differently; the offset members are there for callers that need
        ///   the value they wrote.
        /// </summary>
        [Fact]
        public void ADateTimeComesBackAsTheSameInstantInUniversalTime()
        {
            var local = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Local);

            var readBack = new PdfDate(new PdfDate(local).ToString()).Value;

            readBack.Kind.Should().Be(DateTimeKind.Utc);
            readBack.Should().Be(local.ToUniversalTime());
        }
    }
}
