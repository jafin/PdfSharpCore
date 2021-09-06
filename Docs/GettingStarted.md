# Getting Started Example

You'll need a PDF document:

```c#
PdfDocument document = new PdfDocument();
```

And you need a page:

```c#
PdfPage page = document.AddPage();
```
Drawing is done with an XGraphics object:

```
XGraphics gfx = XGraphics.FromPdfPage(page);
```

Then you'll create a font:

```c#
XFont font = new XFont("Verdana", 20, XFontStyle.Bold);
```

And you use that font to draw a string:

```c#
gfx.DrawString("Hello, World!", font, XBrushes.Black,
new XRect(0, 0, page.Width, page.Height),
XStringFormat.Center);
```

When drawing is done, write the file:

```c#
string filename = "HelloWorld.pdf";
document.Save(filename);

A PC application might show the file:

```c#
Process.Start(filename);
```