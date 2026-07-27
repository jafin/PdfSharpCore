# PdfSharpCore

[![NuGet Version](https://img.shields.io/nuget/v/PdfSharpCore.svg)](https://www.nuget.org/packages/PdfSharpCore/)
[![CI](https://github.com/ststeiger/PdfSharpCore/actions/workflows/build.yml/badge.svg)](https://github.com/ststeiger/PdfSharpCore/actions/workflows/build.yml)
[![codecov.io](https://codecov.io/github/ststeiger/PdfSharpCore/coverage.svg?branch=master)](https://codecov.io/github/ststeiger/PdfSharpCore?branch=master)

**PdfSharpCore** is a partial port of [PdfSharp.Xamarin](https://github.com/roceh/PdfSharp.Xamarin/) for .NET Standard.
Additionally MigraDoc has been ported as well (from version 1.32).
The core `PdfSharpCore` package carries no imaging or font dependency of its own. Pick a backend package and register it once at startup.


## Backends

| Package | Backend | License | Notes |
| --- | --- | --- | --- |
| `PdfSharpCore.Skia` | [SkiaSharp](https://github.com/mono/SkiaSharp) | MIT | Default. Native library — see below. |
| `PdfSharpCore.ImageSharp` | [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) / [Fonts](https://github.com/SixLabors/Fonts) | Apache-2.0 | Pinned to the Apache-2.0 licensed 2.1.x / 1.0.x lines. |

Register the backend before creating any font or loading any image:

```csharp
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PdfSharpCore.Fonts;
using PdfSharpCore.Utils;

GlobalFontSettings.FontResolver = new SkiaFontResolver();
ImageSource.ImageSourceImpl = new SkiaImageSource();
```

Both throw a descriptive `InvalidOperationException` if you use them without registering a backend first.

### SkiaSharp native assets

SkiaSharp is a native library, so an application using `PdfSharpCore.Skia` must also reference the
native asset package for each platform it runs on. PdfSharpCore deliberately does not pull these in,
so that you can choose the right Linux variant:

```xml
<PackageReference Include="SkiaSharp.NativeAssets.Win32" Version="4.150.1" />
<PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" Version="4.150.1" />
<PackageReference Include="SkiaSharp.NativeAssets.macOS" Version="4.150.1" />
```

Use `SkiaSharp.NativeAssets.Linux` instead of `...NoDependencies` if `libfontconfig1` is available
on your Linux image. The `PdfSharpCore.ImageSharp` backend is fully managed and needs none of this.


## Table of Contents

- [Documentation](docs/index.md)
- [Backends](#backends)
- [Example](#example)
- [Contributing](#contributing)
- [License](#license)


## Example

The following code snippet creates a simple PDF-file with the text 'Hello World!'.
The code is written for a .NET 8 console app with top level statements.

```csharp
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Utils;

GlobalFontSettings.FontResolver = new SkiaFontResolver();

var document = new PdfDocument();
var page = document.AddPage();

var gfx = XGraphics.FromPdfPage(page);
var font = new XFont("Arial", 20, XFontStyle.Bold);

var textColor = XBrushes.Black;
var layout = new XRect(20, 20, page.Width, page.Height);
var format = XStringFormats.Center;

gfx.DrawString("Hello World!", font, textColor, layout, format);

document.Save("helloworld.pdf");
```

## Running the tests

`dotnet test` needs no setup. Ghostscript, used to rasterize PDFs for the visual comparison tests,
comes from the `Ghostscript.NativeAssets` package, so there is nothing to install on Windows.
On Linux and macOS ImageMagick invokes the system `gs` delegate instead, so install Ghostscript
through your package manager (`apt-get install ghostscript`, `brew install ghostscript`) if you
want those tests to run.

The visual comparison tests in `XTextFormatterTest` compare against reference images rendered on
Linux. Text rasterizes differently elsewhere because a different set of system fonts is installed,
so on other platforms they report as skipped with the reason rather than failing. CI runs on Linux
and remains the authority on rendering; if you change text layout, check its result.


## Contributing

We appreciate feedback and contribution to this repo!


## License

This software is released under the MIT License. See the [LICENSE](LICENCE.md) file for more info.

PdfSharpCore relies on the following projects, that are not under the MIT license:

* *SixLabors.ImageSharp* and *SixLabors.Fonts*
  * SixLabors.ImageSharp and SixLabors.Fonts, libraries which PdfSharpCore relies upon, are licensed under Apache 2.0 when distributed as part of PdfSharpCore. The SixLabors.ImageSharp license covers all other usage, see https://github.com/SixLabors/ImageSharp/blob/master/LICENSE