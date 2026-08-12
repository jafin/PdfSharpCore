Group 1 fixes ground that is currently soft and must land before anything stands on it. Group 2 is
the layout machinery, group 3 the document object model, group 4 wires the two together. Nothing in
groups 1 to 3 changes the output of any existing document.

## 1. Make the ground firm first

- [x] 1.1 Pin the current output: render a corpus of MigraDoc documents — floating images, text
      frames, tables crossing pages, multi-column text — save them, and keep the bytes to compare
      against at the end of every group. This change is layout, and layout regressions are silent.
      **10 documents, 14 pages**, pinned in `Assets/Layout/migradoc-baseline.txt`.
- [x] 1.2 Establish what `Area.GetFittingRect` returning null means and make `ParagraphRenderer`
      honour it. It returns null past the bottom of the area today, the code carries a standing
      `// BUG: Code removed because null is not handled in caller`, and one caller contains an
      `if (fittingRect == null) GetType();` whose only purpose is to be a breakpoint. Contract
      written onto `Area.GetFittingRect`; the breakpoint replaced by the decision it stood in for;
      the four call sites that dereferenced without looking routed through `FittingRectOrBounds`,
      which is the fallback the rendering phase already reached for under a comment.
- [x] 1.3 Test the null path against the plain `Rectangle`, before any new area exists. A band with
      no room must advance the paragraph rather than fail. Doing this first is what makes a later
      failure in the obstacle area attributable to the obstacle area.
- [x] 1.4 Re-run 1.1. Identical bytes. If they are not, the cause is in this group and nowhere else.
      **Identical**, all 10.

## 2. An area that knows about obstacles

- [x] 2.1 Add the `Area` implementation holding a base rectangle and a list of obstacles, whose
      `GetFittingRect(y, height)` returns the widest clear span in the band. **Widest span, not all
      spans**: the method returns one `Rectangle`, and text that hops across a pull quote and back
      mid-line is unreadable anyway. `ObstructedArea`, with the obstacles copied in so a caller
      still holding a rectangle cannot move one out from under text laid out around it.
- [x] 2.2 Return null where a band is entirely blocked, which is the case group 1 made safe.
- [x] 2.3 Keep the obstacle area out of `Unite`. Uniting with it yields a plain rectangle covering
      both, which is what every caller of `Unite` already assumes — its own comment concedes it is
      "of course not correct" and takes bounding boxes. Write down that this is deliberate.
- [x] 2.4 Unit-test the area directly, with no renderer in sight: an obstacle at the left, at the
      right, in the middle, spanning the full width, above the band, below the band, and two
      obstacles at once. This is the piece where correctness is cheap to establish and expensive to
      debug through a rendered page. **20 tests.** Reached through `AreaProbe`, which keeps the
      reflection in one file rather than adding an `InternalsVisibleTo` this repository does not
      use; `FittingRectContractTests` was moved onto it too.

## 3. The document object model

- [x] 3.1 Add the `WrapStyle` values. `Left` and `Right` name the side the **text** occupies; say so
      in the XML documentation, because the opposite reading is equally natural and a caller who
      guesses wrong gets a page that looks deliberate and is backwards. `Left`, `Right`,
      `Largest` and `Both`, appended so the three that came before keep their numbers. `Both` and
      `Largest` lay out alike today, because a line is given one span rather than every span; they
      are kept apart because they say different things and would part company if that changed.
- [x] 3.2 Check the values through MDDDL, both ways. The DOM's serialisation is generated — read
      `docs/specs/generated-serialization.md` first and verify rather than assume. The parser has a
      generic enum path driven by the value descriptor (`DdlParser.ParseEnumAssignment`), so the new
      values need no generator change — but that is now tested rather than read.
- [x] 3.3 Test the round trip, and test that the existing values still mean exactly what they meant.
      **18 tests**: every value round-trips, is written by name rather than by number, carries its
      four distances with it, and an unknown name is refused rather than taken for the default. That
      the old values still mean what they meant is what the pinned corpus says - it renders a
      `TopBottom` frame and a `Through` frame, and both are byte-identical.

## 4. Wire it up

- [ ] 4.1 Return the side values from `ShapeRenderer.GetFloating`, which can return only `TopBottom`
      or `None` today.
- [ ] 4.2 Teach `TopDownFormatter` the difference. Three lines ask `Floating != Floating.None`; a
      side-floating element has to become an obstacle in the area the following elements are laid
      out in, rather than something they are placed after.
- [ ] 4.3 Give the obstacle the shape's rectangle grown by the four `WrapFormat` distances, so that
      all four start meaning something. Two of them have never meant anything.
- [ ] 4.4 Fall back to `TopBottom` for a shape too tall for its area or one that would span a page
      break, rather than producing an obstacle that outlives the area holding it.
- [ ] 4.5 Test the requirements in `specs/shape-side-wrap/spec.md` that catch silent wrongness: no
      line drawn across the shape, every word present exactly once, and the lines above and below
      running the full measure.
- [ ] 4.6 Test justified text beside an obstacle by reading the drawn positions. Justification
      stretching to the area's measure rather than the line's produces text that is subtly ragged
      rather than obviously broken, which is the kind of defect that ships.
- [ ] 4.7 Re-run 1.1. A document with no side wrap must still be byte-identical.

## 5. Make the demo tell the truth

- [ ] 5.1 Replace `MagazineDemo`'s hand-split flow around the pull quote with a wrap style. The
      split is currently computed by the caller and is the thing the demo most conspicuously should
      not have to do.
- [ ] 5.2 Update `Shows` on `MagazineDemo` and the remarks that explain the workaround.
- [ ] 5.3 Update `docs/specs/demonstration-app.md`, which lists the pull quote among the things
      arithmetic has to fake.
- [ ] 5.4 Re-run the demo smoke tests. `Magazine` declares two pages, and a wrap that reserves the
      wrong area repaginates it.

## 6. Close out

- [ ] 6.1 Add a line to the release notes, including that a document using a new wrap style cannot
      be read by an older version of the library.
- [ ] 6.2 `./ci-build.ps1` clean and `dotnet test` green on both target frameworks.
- [ ] 6.3 Rasterize a page with each of the four wrap styles and look at all four. A wrap on the
      wrong side is a page that looks deliberate and is backwards, and no assertion catches that.
