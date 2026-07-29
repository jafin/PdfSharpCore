# Spec — What replacing the DOM value model found, and what is left

[`compile-time-dom-value-model.md`](compile-time-dom-value-model.md) replaced the DOM's
reflection-built value model with a Roslyn source generator, and migrated `NEnum` to `TEnum?` on the
way. That work is done: 875 tests pass on `net8.0` and `net10.0`, `EnableTrimAnalyzer` is on with
zero `IL2xxx`, and a natively compiled binary exercises the whole model end to end.

Doing it, and then fixing the first item it unblocked, turned up eight things that were not the
point of the exercise. Two were live defects, one of which crashes through public API. The rest are
latent, or are shapes in the code that only became visible once the reflection stopped hiding them.

This document records those, and what is worth doing next. Nothing here is required by the value
model work — it is all standalone, and can be picked up in any order.

| # | finding | severity | status |
|---|---|---|---|
| F1 | `FormattedText.SetNull()` threw `InvalidCastException` | medium | **fixed** |
| F2 | `DocumentObjectDescriptor.IsNull` discards the answer it computes | low | carried forward, pinned |
| F3 | `FormattedText.IsNull()` can never return true | low | open |
| F4 | Writing through a read-only style silently does nothing | medium | open |
| F5 | `ArrayList.ToArray(Type)` is AOT-unsafe, at seven sites | medium | **done** |
| F6 | Reflection's member order was never specified | — | resolved as a side effect |
| F7 | `FormattedText`'s nine delegating `[DV]` properties are the odd shape in the DOM | low | open |
| F8 | Aliased colours serialize under the name that was not declared first | low | open |

---

## F1. `FormattedText.SetNull()` threw — fixed

`FormattedText` carries `[DV]` on nine properties that delegate to its `Font`. Five are value types
with no null of their own — `Bold`, `Italic`, `Superscript`, `Subscript` (`bool`) and `Underline`
(an enum). The reflection model routed every value type to `ValueTypeDescriptor`, whose `SetNull`
cast to `INullableValue` without checking:

```csharp
new FormattedText().SetNull();
// System.InvalidCastException: Unable to cast object of type 'System.Boolean'
// to type 'MigraDocCore.DocumentObjectModel.Internals.INullableValue'.
```

This was item 3 of [`dom-thread-safety.md`](dom-thread-safety.md), which described the unguarded
cast as reachable only by "the next value type that does not implement the interface". It was
reachable already.

Fixed by `ValueKind.PlainValue`, whose `SetNull` does nothing. Pinned by
`ValueModelKnownDefectsTests.FormattedTextSetNullNoLongerThrows`.

**Why nothing had noticed:** reading is fine — `IsNull` tests the cast before using it — and
serialization never calls `SetNull` on a whole object. Only the whole-object sweep reaches it.

---

## F2. `DocumentObjectDescriptor.IsNull` discards the answer it computes

The property branch computed `val.IsNull()`, threw the result away, and returned `true`:

```csharp
if (val != null)
    val.IsNull();   // <-- result discarded
return true;
```

The generated descriptor does the same thing, deliberately, with a comment saying so. Changing it
during the migration would have meant the parity harness was gating a behaviour change rather than
a behaviour-preserving replacement.

**Blast radius is small.** `Style.Font` is the only `[DV]` property in the DOM whose type is a
`DocumentObject`, and `Meta.IsNull(dom, name)` does not use the descriptor for a `DocumentObject`
member — it calls `GetValue` and asks the object — so `style.IsNull("Font")` answers correctly. Only
the whole-object `Meta.IsNull(dom)` sweep calls it, and there `Style`'s separately tracked
`paragraphFormat` field masks the wrong answer.

### Suggested fix

One line, in `ValueDescriptor.IsNull`'s default branch: drop the `if (!isField) return true;` guard
and let the property fall through to the same `value == null || value.IsNull()` the field branch
uses. Then update `ValueModelKnownDefectsTests.ADocumentObjectPropertyDescriptorAlwaysReportsNull`,
which currently pins the wrong answer.

Verify by checking that `Style.IsNull()` is unchanged for a style with a font — it should be,
because `paragraphFormat` already answers correctly, which is the whole reason this is latent.

---

## F3. `FormattedText.IsNull()` can never return true

A consequence of F1's shape, and visible only once the model had a `Kind` to inspect. Of
`FormattedText`'s thirteen descriptors, **seven can never report null**:

| member | kind | why it is never null |
|---|---|---|
| `Bold`, `Italic`, `Underline`, `Superscript`, `Subscript` | `PlainValue` | a value type with no null; `IsNull` is hardcoded false |
| `FontName`, `Name` | `Leaf` | they read `Font.Name`, whose getter returns `name ?? ""` — never null |

`Meta.IsNull(dom)` returns false on the first non-null member, so:

```csharp
new FormattedText().IsNull();   // false, always, for every FormattedText ever constructed
```

This is pre-existing and preserved — `ValueTypeDescriptor.IsNull` returned false for a non-
`INullableValue` too, and `Font.Name` has always coalesced to `""`. It is recorded here because
`IsNull()` is public, is documented as "determines whether this instance is null (not set)", and for
this one type is a constant.

### Suggested work

Decide what `IsNull()` should mean for a type whose model contains members that cannot be unset.
Two defensible answers:

* **Exclude members that cannot be null** from the whole-object sweep, the way `RefOnly` members are
  already excluded. `PlainValue` is exactly the set that can never contribute a `true`.
* **Leave it**, and document that `FormattedText.IsNull()` is meaningless.

The first is a behaviour change for `FormattedText` and nothing else — no other DOM type has a
`PlainValue` member. Worth a test either way; there is none today.

---

## F4. Writing through a read-only style silently does nothing

Found while writing a test, not while reading code, which is why it is worth recording.

```csharp
var document = new Document();
var builtIn = (Style)document.Styles[0];      // DefaultParagraphFont, IsReadOnly == true

builtIn.Font.Bold = true;
builtIn.Font.Bold;                             // false
```

`Style.ParagraphFormat`'s getter returns `paragraphFormat.Clone()` when `readOnly` is set, and
`Style.Font` is `ParagraphFormat.Font`. So the assignment lands on a clone that is discarded the
moment the expression ends. No exception, no diagnostic, no effect.

The intent — that built-in styles cannot be mutated — is right. Enforcing it by handing back a
throwaway and letting the caller write to it is not: the caller has no way to tell success from
silence, and the natural reading of `document.Styles[0].Font.Bold = true` is that it worked.

### Suggested fix

Throw from the setters of a read-only style rather than cloning on read, or at minimum throw from
`ParagraphFormat`'s and `Font`'s setters when their owning style is read-only. Either changes
behaviour for anyone currently relying on the silence, so it wants a release note.

Cheapest useful step is a test that pins the current behaviour, so that whichever way it is decided
is deliberate.

---

## F5. `ArrayList.ToArray(Type)` is AOT-unsafe, at seven sites

Found by the native publish, not by the trim analyzer. `ArrayList.ToArray(Type)` carries
`RequiresDynamicCode` because it constructs an array type at run time, which an AOT compiler cannot
always have generated code for.

| file | line |
|---|---|
| `MigraDoc.DocumentObjectModel/Paragraph.cs` | 608 |
| `MigraDoc.DocumentObjectModel.Visitors/PdfFlattenVisitor.cs` | 92 |
| `MigraDoc.Rendering/FormattedCell.cs` | 172 |
| `MigraDoc.Rendering/FormattedDocument.cs` | 254 |
| `MigraDoc.Rendering/FormattedHeaderFooter.cs` | 88 |
| `MigraDoc.Rendering/FormattedTextArea.cs` | 134 |
| `MigraDoc.Rendering/FormattedTextFrame.cs` | 86 |

`MigraDocCore.AotSmokeTest` passes today because the array types involved happen to be rooted. That
is luck, not design, and it is exactly the class of failure the smoke test exists to catch.

### Suggested fix

Mechanical, per site:

```csharp
// before
return (RenderInfo[])renderInfos.ToArray(typeof(RenderInfo));
// after
var result = new RenderInfo[renderInfos.Count];
renderInfos.CopyTo(result);
return result;
```

Seven edits, no behaviour change, and it takes the AOT publish to warning-free. This is the highest
value-for-effort item in this document.

### Done

All seven fixed. Six use `CopyTo` into a statically typed array; `PdfFlattenVisitor`'s is the one
that differs — its `ArrayList` holds boxed `int`, so it unboxes one element at a time rather than
relying on `Array.Copy`'s unboxing rules, which is a detail better written down than inferred.

`FormattedDocument` also moved from `ContainsKey` followed by an indexer to a single `TryGetValue`,
since the fix needed the value in a local anyway.

The AOT publish is now **clean of both `IL2xxx` and `IL3050`**, and the native binary still passes
all 25 checks. All 880 tests pass — the rendering tests exercise every one of these paths.

**Consequence worth noting:** `MigraDocCore.AotSmokeTest` is now in `PdfSharpCore.slnx`. It was kept
out precisely because these seven warnings would have appeared on every developer build; with them
gone, having the project in the solution is a benefit rather than a cost. It is the only place in
the repo where the DOM and the renderer are analysed together for AOT safety, so a new warning there
now means a real hazard somewhere below it. Verified the full solution build is warning-free with it
included.

**The wider version.** `ArrayList` and `Hashtable` account for ~50 uses across the DOM and the
renderer. The value model shed its two (`ValueDescriptorCollection`); the rest are untouched.
Migrating them to `List<T>` and `Dictionary<K,V>` would remove this whole class of problem, along
with the boxing and the absent type safety, but it is a large mechanical change across code with no
dedicated tests — the renderer especially. Worth doing as its own tracked piece of work, not folded
into anything.

---

## F6. Reflection's member order was never specified — resolved as a side effect

`Meta` built its table from `Type.GetRuntimeFields()` then `Type.GetProperties(...)`. The CLR
specifies no order within either call, so the descriptor order was whatever the runtime happened to
produce. Nothing depended on it — `Meta.SetNull(dom)` sets all, `Meta.IsNull(dom)` short-circuits on
the first non-null, and nothing outside `Meta` enumerated `ValueDescriptors` — but it was not
reproducible build to build.

The generated model orders members base-class first, then source position within each class. No
action needed; recorded because "the order changed" is a true statement about this migration and
someone will eventually notice.

---

## F7. `FormattedText`'s delegating `[DV]` properties are the odd shape in the DOM

Every other `[DV]` member in the DOM is a field holding state. `FormattedText` has nine that are
properties holding none — they read and write the corresponding member of its `Font`:

```csharp
[DV]
public bool Bold
{
    get => Font.Bold;
    set => Font.Bold = value;
}
```

They exist so that DDL can write `Bold = true` directly inside a `FormattedText` block rather than
inside a nested `Font` block. That is a real requirement and the properties are a reasonable way to
meet it — but they are the direct cause of F1 and F3, and they are why `MDG002` could not be an
error over this tree as the value model spec originally designed it.

### Suggested work

Not a fix, an evaluation. If DDL's `FormattedText { Bold = true }` could be handled by the parser
resolving `Bold` against the nested `Font`'s model — which it already knows how to do for dotted
names — the nine properties could lose their `[DV]`, `ValueKind.PlainValue` would have no members
at all, and F1 and F3 would both disappear at the source rather than being worked around.

Whether that is possible without breaking DDL compatibility is the open question, and it needs
someone to read `DdlParser`'s block handling carefully. Worth an hour before anyone acts on F3.

---

## F8. Aliased colours serialize under the name that was not declared first

Found while fixing `dom-thread-safety.md` item 1, by writing a test that asserted the obvious thing
and watching it fail.

```csharp
Colors.Aqua.ToString();       // "Cyan"
Colors.Fuchsia.ToString();    // "Magenta"
```

`ColorName` declares `Aqua = 0xFF00FFFF` at line 42 and `Cyan = 0xFF00FFFF` at line 60, so the
table can only hold one of them and the guard that builds it keeps the first one it meets. The
natural assumption is that this means the first declared. It does not: `Enum.GetNames` orders by
**value**, not by declaration, and for two names sharing a value the tie-break is unspecified. In
practice `Cyan` and `Magenta` win.

**This is not a regression.** The old `ContainsKey`/`Add` guard and the new `TryAdd` both keep the
first entry in the same iteration order, so the fix is faithful. It is recorded because a document
that assigns `Colors.Aqua` serializes as `Cyan`, round-trips back as `Cyan`, and nothing in the
library says so.

### Suggested work

Decide whether it matters. Two positions, both defensible:

* **It does not** — the colours are genuinely equal, the DDL is correct, and a round trip is
  lossless in value if not in spelling. Document it and move on.
* **It does** — a caller who writes `Colors.Aqua` and reads back `Cyan` has been surprised, and the
  choice is currently made by an unspecified sort. If so, drive the table from an explicit ordered
  list rather than from `Enum.GetNames`, so the winner is chosen rather than inherited.

`ColorToStringTests.AliasedColoursAgreeOnOneName` asserts only that both alias to the *same* name
and that it is one of the pair, deliberately, so that this does not become a test of the runtime's
enum ordering.

---

## Still open in `dom-thread-safety.md`

Three items from that document were not touched by this work and remain scheduled there:

| item | what | severity | status |
|---|---|---|---|
| 1 | `Color.ToString` publishes its colour-name table before filling it | high | **done** |
| 4 | `DocumentInfo` decides what to write from emptiness, not from nullness | low | open |
| 6 | `CS8073` is a warning, and it is the only guard against a silent bug | medium | open |

**Item 1 is done.** The table is now a `static readonly Dictionary` built by a static initializer,
`DomSerializationCollection` is deleted, and the seven DOM test classes it serialized run in
parallel again — 880 tests, three consecutive clean runs on both target frameworks. That was the
real confirmation the item asked for: the `Color` race was the only thing those tests were being
serialized to avoid. It also produced F8 above.

Item 6 is one line in `Directory.Build.props` and guards every future mechanical edit of the kind
this work did a lot of.

---

## Gaps in what was built

Honest list of what the value model work left thin.

**The generator has no unit tests.** `Emitter` was deliberately written free of Roslyn types so it
could be exercised with hand-built `DomTypeModel`s and no compilation, and that was never done. The
339 parity assertions test the *output* against a live compilation, which is the more valuable end,
but a snapshot test over `Emitter.Emit` would catch formatting and escaping regressions far faster
than a full build. `Verify` is already used elsewhere in the repo.

**The diagnostics are untested.** MDG001–MDG006 are implemented and none has a test proving it
fires. `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing` is the usual way; each is a handful
of lines. MDG002 in particular is the one that replaced a `Debug.Assert(false)`, and nobody has
confirmed it actually errors on an unhandled member type.

**The AOT smoke test's Linux leg is unverified.** It was written and confirmed on `win-x64` — a
5.1 MB native binary passing all 25 checks. The CI step publishes `linux-x64` with `clang` and
`zlib1g-dev`, and has not run yet. If it fails, it will fail on toolchain setup rather than on
anything in this work.

**Incremental caching is unmeasured.** The generator is built to the usual discipline — value-
equatable models, `EquatableArray`, no Roslyn types in the pipeline — but nobody has attached a
profiler or checked that an edit to an unrelated file produces a cache hit. The `Collect()` barrier
is a deliberate cost and its size is unknown.

---

## Suggested order

1. ~~**`dom-thread-safety.md` item 1** — `Color.ToString`.~~ **Done.**
2. **F5, the seven `ToArray(Type)` sites** — mechanical, no behaviour change, takes the AOT publish
   to warning-free.
3. **`dom-thread-safety.md` item 6** — `CS8073` as an error. One line, guards everything after it.
4. **F2** — one line plus a test update, removes a discarded result.
5. **Generator diagnostic tests** — the cheapest way to stop MDG001–006 being decorative.
6. **F7's evaluation**, then **F3** and **F4** depending on what it concludes.
7. **`dom-thread-safety.md` item 4**, and the wider `ArrayList`/`Hashtable` migration, as their own
   pieces of work — both change emitted output or touch untested code.

Deliberately not listed: generating the 67 `Serialize` methods. It is the natural next use of the
model now that it exists, and §8 of [`compile-time-dom-value-model.md`](compile-time-dom-value-model.md)
explains why it needs its own spec rather than a line on a list — the irregular cases are the
majority of the work, not the edge.
