using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using PdfSharpCore.Pdf;

namespace SampleApp.Infrastructure;

/// <summary>
///   One demonstration: a name on the command line, a description of what to look for, and a
///   document it knows how to build.
/// </summary>
/// <remarks>
///   <para>
///     A demo builds and returns a document. It does not save it, does not decide where it goes,
///     does not delete what was there before, and above all does not register a font resolver or an
///     image source. Those are the composition root's business, and keeping them out of here is
///     what lets the smoke test run every demo inside a test host that has already installed
///     backends of its own.
///   </para>
///   <para>
///     Everything a demo needs must live in the demo's own file. The source printed to the terminal
///     is that file, so a demo that reaches for a shared drawing helper is a demo whose printed
///     source no longer tells the whole story.
///   </para>
/// </remarks>
public abstract class PdfDemo
{
    /// <param name="sourceFilePath">
    ///   Never passed. The compiler fills it in at the call site, which is the <c>: base()</c>
    ///   written in the derived class's file.
    /// </param>
    protected PdfDemo([CallerFilePath] string sourceFilePath = "")
    {
        SourceFilePath = sourceFilePath;
        SourceFileName = Path.GetFileName(sourceFilePath);

        // Caller info is recorded where the base constructor is called from, so a derived class
        // must call it explicitly for that to be its own file. Getting this wrong prints one demo's
        // source above another demo's output, which is the single failure the whole source-printing
        // design exists to prevent - so it is worth stopping for rather than quietly showing the
        // wrong code.
        if (SourceFileName.Length == 0 || SourceFileName == "PdfDemo.cs")
        {
            throw new InvalidOperationException(
                $"{GetType().Name} must declare a constructor of its own - "
                + $"'public {GetType().Name}() : base() {{ }}' - so that [CallerFilePath] records "
                + "the file the demo is written in rather than PdfDemo.cs.");
        }
    }

    /// <summary>
    ///   The name on the command line, and the stem of the file written: <c>Fonts</c> produces
    ///   <c>Fonts.pdf</c>. Matched case-insensitively, so it may be capitalised for reading.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>One line, shown by <c>list</c> and above the demo's source when it runs.</summary>
    public abstract string Summary { get; }

    /// <summary>
    ///   What a reader should look for in the PDF. No assertion can check that a demo still
    ///   demonstrates what it claims, so this is the thing to read it against.
    /// </summary>
    public abstract IReadOnlyList<string> Shows { get; }

    /// <summary>
    ///   Pages the demo produces. Pinned rather than derived so that the smoke test notices when a
    ///   change to the layout engine silently repaginates a document.
    /// </summary>
    public abstract int PageCount { get; }

    /// <summary>The file this demo is written in, as it was at build time.</summary>
    public string SourceFilePath { get; }

    /// <summary>Just the file name, which is how the embedded copy of the source is found.</summary>
    public string SourceFileName { get; }

    /// <summary>Builds the document. The base class saves it.</summary>
    protected abstract PdfDocument Build(DemoContext context);

    public DemoResult Run(DemoContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        string path = context.PrepareOutputPath(Name);

        Stopwatch stopwatch = Stopwatch.StartNew();
        int pages;
        using (PdfDocument document = Build(context))
        {
            pages = document.PageCount;
            document.Save(path);
        }
        stopwatch.Stop();

        return new DemoResult(Name, path, pages, new FileInfo(path).Length, stopwatch.Elapsed);
    }
}
