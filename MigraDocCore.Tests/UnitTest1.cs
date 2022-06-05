using MigraDocCore.DocumentObjectModel;
using MigraDocCore.Rendering;
using MigraDocCore.Tests.Helpers;
using Xunit;

namespace MigraDocCore.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            // Create a new MigraDoc document
            Document document = new Document();
            // Add a section to the document
            Section section = document.AddSection();
            // Add a paragraph to the section
            Paragraph paragraph = section.AddParagraph();
            // Add some text to the paragraph
            paragraph.AddFormattedText("Hello, World!", TextFormat.Italic);
            paragraph.AddImage(PathHelper.GetInstance().GetAssetPath("image.jpg"));
            
            PdfDocumentRenderer renderer = new PdfDocumentRenderer(true);
            renderer.Document = document;
            renderer.RenderDocument();
            renderer.PdfDocument.Save("test-migradoc.pdf");
        }
    }
}