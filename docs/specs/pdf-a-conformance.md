# Proposal — PDF/A conformance, XMP metadata and hybrid e-invoicing

What archival and e-invoice output covers, and what it deliberately leaves out.
Gap **G4** of the competitive gap analysis.

| item | what | status |
|---|---|---|
| 1 | An XMP metadata writer, synchronised with the info dictionary | done, **and PDF/UA now shares it** |
| 2 | Output intent with an embedded ICC profile | done, **caller supplies the profile** |
| 3 | `PdfDocumentOptions.Conformance` that **enforces** rather than labels | done, **partially** |
| 4 | PDF/A-3 attachments — `/AFRelationship` and catalog `/AF` | not started |
| 5 | `PdfSharpCore.EInvoice` — a ZUGFeRD / Factur-X helper | not started |

Covered by `PdfSharpCore.Test/IO/XmpMetadataTests.cs`.

## What is honestly not finished

**No ICC profile ships.** Item 2 embeds the bytes it is given and there are none in the box, so a
document claiming conformance has to be handed a profile. The proposal assumed an sRGB
IEC61966-2.1 profile could simply be embedded as a resource; that needs a vetted, redistributable
asset and a licence check, which is a decision about what the repository ships rather than a piece
of code. Until then the failure is loud — saving without one throws and the message says so.

**Enforcement is partial, and the code says which parts.** These are checked: no encryption, a title
present, an output intent profile present, embedded files only under PDF/A-3, and the version floor
and version ceiling for the claimed part — PDF/A-1 is refused outright for a document already past
PDF 1.4, and for one asking for a cross-reference stream, which is a PDF 1.5 construction. These are
**not** checked: no transparency and no JPXDecode under PDF/A-1 (both
need a walk of every page's resources — `PdfTransparencyDetector` answers the question for one
XObject, not for a page), and `/Interpolate true` on images. A successful save is therefore not a
validator's verdict, and `Enforce` says so in its own remarks rather than leaving silence to imply
otherwise.

**veraPDF is not in CI.** Without it the conformance claim is self-certified, which is worth little.
That remains the right next step and it is still a Java/Docker step added to a build that is
currently pure .NET plus Ghostscript. It is now wanted by two features rather than one — see
`docs/specs/tagged-pdf-accessibility.md`, whose PDF/UA claim is self-certified in the same way and
for the same reason.

## What was added afterwards

`XmpMetadata` gained `UAConformance`, writing `pdfuaid:part`, when tagged output learned to claim
PDF/UA-1. The proposal above said the packet was built to be extended and named PDF/UA as the first
thing that would extend it, which turned out to be right — the whole change was one property, one
`rdf:Description`, and one line in `PdfConformanceWriter` setting it after the customisation callback
for the reason the callback comment already gave. The two claims are independent: PDF/A says the file
will still open in fifty years, PDF/UA says it can be read aloud, and a document may make both.

---

## The defect

`/Metadata` appears as a key-name constant on the catalog, on `PdfPage` and on `PdfImage`. There is no
XMP writer behind any of them, no output intent, and no notion of conformance at all. A document this
library produces cannot say what standard it claims to meet, which means it meets none of them.

## Why this ranks above the larger items

Three things line up, and rarely do.

**The repo is unusually close already.** `Pdf.Advanced/PdfEmbeddedFile.cs` and `PdfFileSpecification.cs`
exist. Fonts are always embedded, with no setting to disable it — which is a PDF/A requirement that
most libraries have to add and this one cannot violate. What is missing is metadata, an output intent,
and a refusal to write a file that breaks the rules.

**The deadlines are fixed, public and already running.** ZUGFeRD / Factur-X embeds a UN/CEFACT CII XML
invoice inside a **PDF/A-3** container. In Germany: receipt capability mandatory since 1 January 2025;
issuing mandatory above €800K turnover from 1 January 2027; all domestic B2B from 1 January 2028.
ZUGFeRD 2.4 is effective January 2026.

**PDF/A-3 is the only PDF/A profile that legally carries attachments.** That is the whole basis of the
hybrid invoice format, and it happens to be the profile this codebase is nearest to.

It also unblocks Stage C of `docs/specs/tagged-pdf-accessibility.md`, which needs the XMP writer to
declare the PDF/UA-1 identifier.

---

## Item 1 — the XMP packet

An RDF/XML document in a stream on the catalog, uncompressed, wrapped in the packet markers a
byte-scanner can find without parsing the PDF:

```xml
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
      xmlns:dc="http://purl.org/dc/elements/1.1/"
      xmlns:pdfaid="http://www.aiim.org/pdfa/ns/id/">
    <pdfaid:part>3</pdfaid:part>
    <pdfaid:conformance>B</pdfaid:conformance>
    <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Invoice 2026-0042</rdf:li></rdf:Alt></dc:title>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
```

`PdfSharpCore.Pdf.Metadata.XmpMetadata`, built to be **extended rather than replaced** — PDF/UA-1 adds
its own identifier, ZUGFeRD adds a whole extension schema, and a caller may want their own namespace.

**The trap is synchronisation.** `Pdf/PdfDocumentInformation.cs` holds `/Title`, `/Author`, `/Subject`,
`/Keywords`, `/CreationDate`, `/ModDate`. XMP holds the same facts under different names. A validator
compares them and complains if they disagree, so the two must be written from one source at save time,
not maintained in parallel and hoped about.

## Item 2 — the output intent

Every device-dependent colour space (`DeviceRGB`, `DeviceCMYK`) needs an output intent naming an ICC
profile, and the profile must be **embedded**, not referenced:

```text
Catalog /OutputIntents [ <</Type /OutputIntent  /S /GTS_PDFA1
                           /OutputConditionIdentifier (sRGB IEC61966-2.1)
                           /DestOutputProfile ──► ICC stream (~3 KB, embedded)>> ]
```

Ship sRGB IEC61966-2.1 as an embedded resource — it is small and redistributable — and let a caller
supply a CMYK profile for print work. Note `Pdf/PdfDocumentOptions.cs` already has `ColorMode`, which
this has to agree with.

## Item 3 — enforce, do not label

```csharp
document.Options.Conformance = PdfConformance.PdfA3B;
```

The failure mode to avoid is a library that stamps `pdfaid:part 3` on a file and leaves the caller to
discover from a validator, or from their customer, that it does not conform. So the mode **refuses at
`Save`**, naming the specific rule:

| rule | applies to | already true here? |
|---|---|---|
| No encryption | all parts | must be enforced — `Pdf.Security` is fully functional |
| Every font embedded, subsets complete | all parts | **yes** — no setting to disable it |
| Composite fonts carry `/ToUnicode` | all parts | **yes** — `PdfToUnicodeMap` |
| Document `/ID` present | all parts | yes — `_trailer.CreateNewDocumentIDs()` |
| Title in both info dictionary and XMP | all parts | item 1 |
| Output intent for device colour | all parts | item 2 |
| No transparency, no JPXDecode | **A-1 only** | must be enforced — the repo has soft masks and transparency groups |
| No `/Interpolate true` on images | all parts | must be enforced |
| No embedded files | **A-1 outright; A-2 unless the file is itself PDF/A**; A-3 permits any | refused for A-1 and A-2 |

PDF/A-2's embedded-file rule is the one place this is stricter than the standard, and deliberately.
A-2 permits an embedded file that is itself PDF/A, and nothing here can establish that a given
attachment is — so the claim is refused rather than made on trust. A document that needs both an
attachment and a conformance claim it can stand behind should claim PDF/A-3, which has no such
restriction and is what the hybrid e-invoice profiles are built on.

The A-1 transparency rule is the one that will bite: this fork has transparency groups, soft masks and
gradient soft masks, and a document using them cannot be PDF/A-1. Saying so at save time, in a message
that names the feature and the page, is the entire value of the option.

## Items 4 and 5 — the hybrid invoice

PDF/A-3 permits attachments, and an e-invoice is a PDF/A-3 with one:

```text
Catalog /AF [ ──► /Filespec  /F (factur-x.xml)
                            /UF (factur-x.xml)
                            /AFRelationship /Data      ← this is the new part
                            /Desc (Factur-X invoice)
                            /EF <</F ──► embedded file stream>> ]
```

`/AFRelationship` and the catalog-level `/AF` array are the missing pieces; the file specification and
embedded file objects already exist.

`PdfSharpCore.EInvoice` is then thin: attach the XML with the right filename and relationship, emit the
ZUGFeRD XMP extension schema (profile, version, conformance level), set the conformance mode.

**Generating the CII XML itself is out of scope.** EN 16931 semantics, profile validation and the
country-specific rules are somebody else's library and a permanent maintenance liability; the
documentation should name one rather than absorb it.

---

## What this deliberately does not cover

- **PDF/A-1a, A-2a, A-3a** — the accessible conformance levels. They require a full tagged structure
  tree, so they are gated on `docs/specs/tagged-pdf-accessibility.md`. The `B` levels are what
  archival and e-invoicing actually ask for; `A` follows for free once tagging lands.
- **PDF/A-4** (ISO 19005-4:2020). Newer, less demanded, and it assumes PDF 2.0 — so it is also gated on
  `docs/specs/cross-reference-streams.md`.
- **Converting an existing PDF to PDF/A.** A different and much harder problem: re-embedding fonts that
  are not there, converting colour spaces, flattening transparency. This proposal is about producing a
  conforming file, not repairing one.
- **Generating EN 16931 invoice XML.** See above.
- **PDF/X** (print). Same machinery, different profile; cheap to add afterwards if anyone asks.

## Tests

`PdfSharpCore.Test`. Golden XMP packets — the format is stable enough that byte comparison is fair, and
it catches accidental namespace churn. A Factur-X round-trip fixture: build, save, reopen, pull the
attachment back out, assert the relationship.

For real validation, **veraPDF as a container step in CI**. This is shared with
`docs/specs/tagged-pdf-accessibility.md` and is the same trade-off: it adds a Java/Docker dependency to
a build that is currently pure .NET plus Ghostscript. Worth it — a self-certified conformance claim is
not worth much — but it is a genuine CI-complexity cost and should be decided once, for both.

## Related

- `docs/specs/tagged-pdf-accessibility.md` — Stage C needs the XMP writer built here.
- `docs/specs/cross-reference-streams.md` — PDF/A-4 and PDF 2.0 are gated on it.
