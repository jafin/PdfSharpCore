# Spec — DOM thread safety and value descriptor follow-ups

Migrating the DOM's nullable wrapper structs to `bool?`, `int?`, `double?` and `string`
([#46](https://github.com/jafin/PdfSharpCore/pull/46)) needed a second test class that serializes
DDL. Two tests then began failing at random, alternately, one per run. Disabling parallelization
made them stop.

That was not a test problem. It was a race in `Color.ToString` that has been in the library since
the port, is reachable from any two threads that serialize a document at once, and sits behind
public API.

| item | what | severity | status |
|---|---|---|---|
| 1 | `Color.ToString` publishes its colour-name table before filling it | high | open |
| 2 | Every `DocumentObject` builds its metadata twice or more under load | low | open |
| 3 | `ValueTypeDescriptor.SetNull` casts to `INullableValue` without checking | low | open |
| 4 | `DocumentInfo` decides what to write from emptiness, not from nullness | low | open |
| 5 | `Meta.IsNull` decides by testing for each descriptor type by name | low | open |
| 6 | `CS8073` is a warning, and it is the only guard against a silent bug | medium | open |
| 7 | `NEnum` is the last wrapper struct standing | low | open |

---

## 1. `Color.ToString` publishes its colour-name table before filling it

`Color.cs:385-397`:

```csharp
public override string ToString()
{
    if (stdColors == null)
    {
        Array colorNames = Enum.GetNames(typeof(ColorName));
        Array colorValues = Enum.GetValues(typeof(ColorName));
        int count = colorNames.GetLength(0);
        stdColors = new Hashtable(count);          // <-- assigned empty
        for (int index = 0; index < count; index++)
        {
            string c = (string)colorNames.GetValue(index);
            uint d = (uint)colorValues.GetValue(index);
            if (!stdColors.ContainsKey(d))
                stdColors.Add(d, c);               // <-- then filled
        }
    }
    ...
}
static Hashtable stdColors;
```

Three separate faults, in one method:

* **The static is assigned before the table has anything in it.** A thread arriving between the
  assignment and the end of the loop sees a non-null `stdColors`, skips construction entirely, and
  reads from a table that is still filling.
* **`ContainsKey` and `Add` are not atomic with respect to each other.** Two threads inside the loop
  can both find a key absent and both add it. `Hashtable.Add` throws on a duplicate key.
* **`Hashtable` supports one writer and many readers, not many writers.** Two threads filling the
  same instance can corrupt it outright.

### Reproduction

`Colors.Black.ToString()` called from 64 threads at once, three runs:

| result | run 1 | run 2 | run 3 |
|---|---|---|---|
| `"Black"` — correct | 31 | 28 | 24 |
| `"RGB(0,0,0)"` — **silently wrong** | 11 | 18 | 18 |
| `ArgumentException: Item has already been added` | 20 | 14 | 21 |
| `""` — empty string | 2 | 4 | 1 |

```csharp
var results = new ConcurrentBag<string>();
Parallel.For(0, 64, _ =>
{
    try { results.Add(Colors.Black.ToString()); }
    catch (Exception ex) { results.Add("THREW " + ex.GetType().Name); }
});
results.Distinct().Should().ContainSingle().And.Contain("Black");
```

The `RGB(0,0,0)` row is the one that matters most. It does not throw and it is not obviously wrong —
a document simply serializes with `RGB(0,0,0)` where it should say `Black`, and the DDL still
parses. The empty string suggests the `Hashtable` itself is being corrupted, not merely read early.

### Fix

Build the table once, off to the side, and publish it complete. A static initializer is the
smallest change and removes the check entirely:

```csharp
static readonly Dictionary<uint, string> stdColors = BuildStandardColors();

static Dictionary<uint, string> BuildStandardColors()
{
    var names = Enum.GetNames(typeof(ColorName));
    var values = (ColorName[])Enum.GetValues(typeof(ColorName));
    var colors = new Dictionary<uint, string>(names.Length);
    for (int index = 0; index < names.Length; index++)
        colors.TryAdd((uint)values[index], names[index]);   // Aqua == Cyan, Fuchsia == Magenta
    return colors;
}
```

The CLR guarantees a static initializer runs once and completes before any thread reads the field,
which is the whole of the problem. `Dictionary` rather than `Hashtable` because the table is never
written after construction, and `TryAdd` says what the existing `ContainsKey` guard meant.

### Acceptance

* The reproduction above returns `"Black"` 64 times out of 64, over repeated runs.
* Keep it as a test — `Color.ToString` is public, and nothing else in the suite calls it
  concurrently.
* `DomSerializationCollection` can then be deleted and the DOM tests allowed to run in parallel
  again, which is the real confirmation.

---

## 2. Every `DocumentObject` builds its metadata twice or more under load

Every class in the DOM caches its reflection metadata the same way — `Document.cs:374-382`, and
identically in `Style`, `Styles` and the rest:

```csharp
internal override Meta Meta
{
    get
    {
        if (meta == null)
            meta = new Meta(typeof(Document));
        return meta;
    }
}
static Meta meta;
```

This is **not** the cause of item 1, and it is worth being clear about that: `Meta`'s constructor
fills its descriptor collection before it returns (`Meta.cs:48-51`), so the instance assigned to the
static is always complete. Two threads racing here get two complete `Meta` objects and one wins.

What it costs is the work: under concurrent first use, every DOM type reflects over its own fields
and properties two or more times and throws all but one result away. It is wasteful rather than
wrong, which is why it is low severity — but it is the same shape of mistake as item 1 and should
be corrected while the area is open.

### Fix

`Lazy<Meta>` with the default thread-safety mode, or a static initializer as in item 1. There are
roughly 60 of these to change, all mechanical.

---

## 3. `ValueTypeDescriptor.SetNull` casts to `INullableValue` without checking

`ValueDescriptor.cs:274-292` casts unconditionally:

```csharp
public override void SetNull(DocumentObject dom)
{
    object val = FieldInfo.GetValue(dom);
    INullableValue ival = (INullableValue)val;    // InvalidCastException if it is not one
    ival.SetNull();
    ...
}
```

while `IsNull` twenty lines below tests first and answers `false` for anything that is not an
`INullableValue` — so the two disagree about what this descriptor is for.

Every value type that currently routes here does implement `INullableValue`, so nothing fails
today. It became briefly reachable during the `NString` migration, when plain `string` members
started matching the `typeof(String)` branch, and was fixed by routing strings to
`NullableMemberDescriptor` instead. The unguarded cast is still there for the next value type that
does not implement the interface.

### Fix

Match `IsNull`: test the cast, and throw something that names the value if it fails.

---

## 4. `DocumentInfo` decides what to write from emptiness, not from nullness

Everywhere else in the DOM, whether an attribute is written is decided by whether the value is
null. `DocumentInfo.Serialize` (`DocumentInfo.cs:131-141`) asks a different question:

```csharp
if (Title != String.Empty)
    serializer.WriteSimpleAttribute("Title", Title);
```

So `document.Info.Title = ""` is set — `IsNull("Title")` is `false` — and is not written, while
`document.FootnoteStartingNumber = 0` is set and *is* written. Round-tripping a document therefore
loses the distinction for that one class.

This predates the migration and is pinned as-is by
`DdlRoundTripTests.AnExplicitlyEmptyStringIsNotWrittenEvenThoughItIsSet`, so the behaviour is at
least recorded rather than accidental.

### Fix

Decide which rule is intended. Changing `DocumentInfo` to test nullness like everything else is the
consistent choice, and would change the DDL emitted for documents that assign `""` — worth a note
in the release notes rather than a silent change.

---

## 5. `Meta.IsNull` decides by testing for each descriptor type by name

`Meta.cs:166`:

```csharp
if (vd is NullableDescriptor || vd is ValueTypeDescriptor || vd is NullableMemberDescriptor)
    return vd.IsNull(dom);
DocumentObject docObj = (DocumentObject)vd.GetValue(dom, GV.ReadOnly);
```

The question being asked is "does this descriptor describe a simple value, or a `DocumentObject`
that the rest of a dotted name is reached through". Asking it by listing types means a new
descriptor is wrong by default: `NullableMemberDescriptor` fell through to the `DocumentObject`
branch and threw `InvalidCastException` on every string until the third name was added by hand.

### Fix

A virtual on `ValueDescriptor` — `IsSimpleValue`, defaulting to `false` and overridden to `true` by
the three — puts the answer next to the type it describes. One call site, so this is about the next
descriptor rather than about this one.

---

## 6. `CS8073` is a warning, and it is the only guard against a silent bug

Comparing a struct that defines `operator ==` against `null` compiles, lifts to `Nullable<T>`, and
is always false. During the `NString` migration a mechanical rewrite turned `style.IsNull` into
`style == null` on four `NEnum` fields. Two failed to compile. The other two compiled, were always
false, and were caught only by `CS8073`.

The same rewrite very nearly caught `LeftPosition.cs:51` and `TopPosition.cs:51`:

```csharp
this.notNull = !value.IsNull;   // value is a Unit parameter, not Point's NDouble field
```

which would have become `value != null` — always **true**, and this time in the opposite direction.

### Fix

```xml
<WarningsAsErrors>$(WarningsAsErrors);CS8073</WarningsAsErrors>
```

in `Directory.Build.props`. The codebase is clean of it today, so this costs nothing now and makes
the next mechanical edit fail loudly instead of quietly.

---

## 7. `NEnum` is the last wrapper struct standing

`NBool`, `NInt`, `NDouble` and `NString` are gone. `INullableValue` remains, implemented by `NEnum`,
`Unit`, `Color`, `LeftPosition` and `TopPosition`.

Four of those five are domain types that carry their own state and should stay. `NEnum` is the odd
one: it exists only to make an enum nullable, which is what `TEnum?` does, but it stores the enum's
`Type` alongside the `int` so that `ValueDescriptor` can read it back — and the `[DV(Type = ...)]`
attribute already carries the same information.

Worth an assessment rather than a commitment. If the `Type` really is redundant with the attribute,
`NEnum` becomes `int?` plus the existing attribute and `INullableValue` shrinks to the four types
that earn it. If it is not, `NEnum` stays and the interface stays with it.

---

## Suggested order

1. **Item 1** on its own, with the concurrency test. It is a live bug behind public API and it is
   the one blocking `DomSerializationCollection` from being deleted.
2. **Item 6**, one line, guards everything after it.
3. **Items 2, 3 and 5** together — all in the value descriptor and metadata layer, all mechanical.
4. **Item 4** on its own, because it changes emitted DDL.
5. **Item 7** last, as an assessment that may conclude "leave it".
