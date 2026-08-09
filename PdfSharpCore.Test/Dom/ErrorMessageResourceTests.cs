using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Internals;
using Xunit;

namespace PdfSharpCore.Test.Dom;

/// <summary>
///   The strongly typed resource class named the resources of the assembly it was generated in
///   before this fork renamed that assembly, so it asked for
///   "MigraDoc.DocumentObjectModel...AppResources" while the assembly embeds
///   "MigraDocCore.DocumentObjectModel...AppResources". Every message it holds threw
///   MissingManifestResourceException instead, so the error you were given was never the error
///   meant for you: asking for an unreadable colour reported a missing resource rather than the
///   colour it could not read.
/// </summary>
public class ErrorMessageResourceTests
{
    [Fact]
    public void EveryMessageCanBeRead()
    {
        // The guard that matters: one lookup name serves all of them, so one of these failing
        // means none of them work.
        var read = () => Messages().Select(message => message.Value).ToList();

        read.Should().NotThrow();
    }

    [Fact]
    public void NoMessageIsEmpty()
    {
        Messages().Should().NotBeEmpty();
        Messages().Should().AllSatisfy(message => message.Value.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void AColourThatCannotBeReadIsNamedInTheComplaint()
    {
        var parse = () => Color.Parse("not a colour");

        parse.Should().Throw<ArgumentException>()
            .WithMessage("*not a colour*");
    }

    [Fact]
    public void AUnitThatCannotBeReadIsNamedInTheComplaint()
    {
        var parse = () => Unit.Parse("not a unit");

        parse.Should().Throw<ArgumentException>()
            .WithMessage("*not a unit*");
    }

    [Fact]
    public void AValueNameThatDoesNotExistIsNamedInTheComplaint()
    {
        var paragraph = new Document().AddSection().AddParagraph();

        var read = () => paragraph.GetValue("NoSuchValue", GV.ReadOnly);

        read.Should().Throw<ArgumentException>()
            .WithMessage("*NoSuchValue*");
    }

    [Fact]
    public void ABaseStyleThatDoesNotExistIsNamedInTheComplaint()
    {
        var document = new Document();

        var define = () => document.Styles.AddStyle("Mine", "NoSuchBaseStyle");

        define.Should().Throw<ArgumentException>()
            .WithMessage("*NoSuchBaseStyle*");
    }

    /// <summary>
    ///   Every message the resource class holds, read the way the library reads them. The class
    ///   is internal to the document object model, so it is reached by name rather than by type.
    /// </summary>
    static IReadOnlyList<KeyValuePair<string, string>> Messages()
    {
        var resources = typeof(Document).Assembly.GetType(
            "MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Resources.AppResources", true);

        return resources
            .GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => new KeyValuePair<string, string>(property.Name, Read(property)))
            .ToList();
    }

    static string Read(PropertyInfo property)
    {
        try
        {
            return (string)property.GetValue(null);
        }
        catch (TargetInvocationException exception)
        {
            // Reflection wraps what the property threw; the test wants to see that instead.
            throw exception.InnerException ?? exception;
        }
    }
}
