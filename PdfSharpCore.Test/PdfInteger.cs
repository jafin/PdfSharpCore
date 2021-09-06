using System;
using Xunit;

namespace PdfSharpCore.UnitTests
{
    public class PdfInteger
    {
        [Fact]
        public void Should_beAbleToConvertToInt32()
        {
            var pdfInt = new PdfSharpCore.Pdf.PdfInteger(10);
            var convertedInt = Convert.ToInt32(pdfInt);
            Assert.Equal(10, convertedInt);
        }
    }
}
