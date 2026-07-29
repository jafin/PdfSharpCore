#region MigraDoc - Creating Documents on the Fly
//
// Authors:
//   Stefan Lange (mailto:Stefan.Lange@PdfSharpCore.com)
//   Klaus Potzesny (mailto:Klaus.Potzesny@PdfSharpCore.com)
//   David Stephensen (mailto:David.Stephensen@PdfSharpCore.com)
//
// Copyright (c) 2001-2009 empira Software GmbH, Cologne (Germany)
//
// http://www.PdfSharpCore.com
// http://www.migradoc.com
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
using System.Reflection;

namespace MigraDocCore.DocumentObjectModel.Internals;

/// <summary>
/// Value descriptor for a member that carries its own null - Nullable&lt;T&gt; for a value type,
/// or a plain reference for a string. The member being null is what "not set" means, so unlike
/// NullableDescriptor there is no wrapper struct to box, mutate through INullableValue and write
/// back; reflection reads and writes the member directly.
/// </summary>
internal class NullableMemberDescriptor : ValueDescriptor
{
    internal override bool IsSimpleValue => true;

    /// <summary>
    /// What GetValue hands back for a member that was never set. The DOM has always answered with
    /// the type's default rather than with null unless GV.GetNull was asked for, so an unset
    /// bool reads as false, an int as 0 and a string as "".
    /// </summary>
    private readonly object valueWhenNull;

    internal NullableMemberDescriptor(
        string valueName,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type valueType,
        Type memberType,
        MemberInfo memberInfo,
        VDFlags flags)
        : base(valueName, valueType, memberType, memberInfo, flags)
    {
        // string has no parameterless constructor to activate, and the DOM has always handed back
        // "" rather than null for an unset one.
        valueWhenNull = valueType == typeof(string) ? "" : Activator.CreateInstance(valueType);
    }

    private object GetMemberValue(DocumentObject dom)
    {
        return FieldInfo != null
            ? FieldInfo.GetValue(dom)
            : PropertyInfo.GetGetMethod(true).Invoke(dom, Type.EmptyTypes);
    }

    private void SetMemberValue(DocumentObject dom, object value)
    {
        if (FieldInfo != null)
            FieldInfo.SetValue(dom, value);
        else
            PropertyInfo.GetSetMethod(true).Invoke(dom, new[] { value });
    }

    public override object GetValue(DocumentObject dom, GV flags)
    {
        if (!Enum.IsDefined(typeof(GV), flags))
            throw new ArgumentException("flags");

        object value = GetMemberValue(dom);
        if (value == null)
            return flags == GV.GetNull ? null : valueWhenNull;
        return value;
    }

    public override void SetValue(DocumentObject dom, object value)
    {
        SetMemberValue(dom, value);
    }

    public override void SetNull(DocumentObject dom)
    {
        SetMemberValue(dom, null);
    }

    /// <summary>
    /// Determines whether the value of the given DocumentObject is null (not set).
    /// </summary>
    public override bool IsNull(DocumentObject dom)
    {
        return GetMemberValue(dom) == null;
    }
}
