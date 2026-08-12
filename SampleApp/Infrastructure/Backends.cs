using System;
using System.Threading;
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PdfSharpCore.Fonts;
using PdfSharpCore.Skia;

namespace SampleApp.Infrastructure;

/// <summary>
///   The two static seams PdfSharpCore leaves for a host to fill: a font resolver and an image
///   source. The core package carries neither an imaging nor a font dependency of its own.
/// </summary>
/// <remarks>
///   <para>
///     Called from exactly one place - the runner, which only <c>Program.Main</c> reaches. No demo
///     calls it, and nothing a demo touches calls it either.
///   </para>
///   <para>
///     That rule is not tidiness. <c>GlobalFontSettings.FontResolver</c> throws
///     "Must not change font resolver after is was once used" as soon as a font has been created,
///     and its getter throws when nothing has been set at all. The test assembly installs its own
///     resolver for everything in it, including these demos once the smoke test runs them. A demo
///     that registered a backend would either throw - and throw only when some other test had
///     already made a font, so passing alone and failing in the suite - or win the race and quietly
///     move every other test in the assembly onto different font metrics.
///   </para>
/// </remarks>
public static class Backends
{
    static int _registered;

    /// <summary>
    ///   Installs the backends, once, and leaves alone any that a host has already chosen.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;

        if (!FontResolverIsSet())
            GlobalFontSettings.FontResolver = new BundledFontResolver();

        ImageSource.ImageSourceImpl ??= new SkiaImageSource();
    }

    /// <summary>
    ///   Whether a resolver is already installed. There is no "is it set" to ask, and the getter
    ///   answers the question by throwing, so the exception is the test. Ugly, and better than the
    ///   alternative: assigning over a host's resolver, which the setter would refuse anyway the
    ///   moment any font had been made.
    /// </summary>
    static bool FontResolverIsSet()
    {
        try
        {
            _ = GlobalFontSettings.FontResolver;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
