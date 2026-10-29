# Job 1 Plan — Core Text Rendering

Goal: given a string, a TTF font and a font size, produce an image of the text, with glyphs laid out by advance width and rasterized by our own code.

SixLabors.Fonts is used **only** to parse the font (metrics + glyph outlines). Layout, flattening, rasterization and blending are ours.

## Decisions

1. **Font library:** `SixLabors.Fonts`, hidden behind `IFontSource`. Only `SixLaborsFontSource` references it.
   - Do **not** use its layout (`TextRenderer` / `TextLayout`) or `SixLabors.ImageSharp.Drawing`.
2. **Glyph outlines are stored in font units, Y-up.** Scaling, Y-flip and pen offset happen only in `PathFlattener`. This keeps `Glyph` size-independent, so it can be cached per `(font, codepoint)` in Job 2.
3. **Support both quadratic and cubic segments** (TTF is quadratic; CFF/OTF can emit cubics).
4. **Rasterize per glyph**, then blend each glyph's coverage onto a shared `Canvas` using "over" blending (`dst = dst·(1−a) + colour·a`) so overlapping glyph boxes don't darken.
5. **Single line only.** Newlines are out of scope for now.
6. **Don't name anything `TextRenderer`.** It clashes with the project namespace and with the SixLabors type. The orchestrator is `RenderPipeline`.
7. **Colours use `System.Drawing.Color`.**
8. **The canvas is always opaque** (alpha 255). Transparent backgrounds can come later with PNG.
9. **Images for visual checks go in `out/`** at the repo root. They're committed (not gitignored) for now, so some example output is in source control.

## Location

- Core code: `TextRenderer/Rendering/` (namespace `TextRenderer.Rendering`)
- Tests: new `TextRenderer.Tests` xUnit project, added to `TextRenderer.sln`
- Sample fonts: `fonts/DejaVuSans.ttf`, `DejaVuSansMono.ttf`, `DejaVuSerif.ttf`

## Types

| Type | Kind | Responsibility |
|---|---|---|
| `IFontSource` | interface | `FontMetrics Metrics { get; }`, `Glyph GetGlyph(int codepoint)` |
| `SixLaborsFontSource` | class | Loads a TTF from a path/stream; maps SixLabors data into our types. Falls back to `.notdef` for missing glyphs |
| `OutlineCollector` | internal class, implements SixLabors `IGlyphRenderer` | Receives `MoveTo` / `LineTo` / `QuadraticBezierTo` / `CubicBezierTo` / `EndFigure`, builds `Contour`s in font units, Y-up |
| `FontMetrics` | record | `UnitsPerEm`, `Ascender`, `Descender`, `LineGap` |
| `Glyph` | record | `AdvanceWidth`, `LeftSideBearing`, `IReadOnlyList<Contour> Contours` (font units) |
| `Contour` / `PathSegment` | records | Start point plus `Line`, `Quad(control, end)`, `Cubic(c1, c2, end)` segments |
| `RenderOptions` | record | `FontSize` (px), `Foreground`, `Background`, `Padding` |
| `TextLayout` | class | Text + `IFontSource` + options → `LayoutResult`: `Scale`, `Baseline`, `Width`, `Height`, `IReadOnlyList<PositionedGlyph(Glyph, PenX)>` |
| `Edge` | record struct | Pixel-space line segment `(x0, y0, x1, y1)` with winding direction |
| `PathFlattener` | class | `Glyph` + scale + pen origin → `Edge[]`. Applies `px = penX + x·scale`, `py = baseline − y·scale`; subdivides curves into lines |
| `Rasterizer` | class | `Edge[]` → coverage (0–1) over the glyph's pixel bounding box. Scanline fill, non-zero winding, 4×4 supersampling for anti-aliasing |
| `Canvas` | class | `Width`, `Height`, RGBA `byte[]`, `Clear(colour)`, `Blend(x, y, coverage, colour)` |
| `IImageEncoder` | interface | `byte[] Encode(Canvas)`, `string ContentType` |
| `PpmEncoder` / `PngEncoder` | classes | PPM (P6) first; PNG (zlib + CRC via `System.IO.Compression`) after |
| `RenderPipeline` | class | Orchestrator: layout → for each glyph (flatten → rasterize → blend) → encode |

## Pipeline

```
string ─► TextLayout ──(Glyph, penX)*──► PathFlattener ──Edge[]──► Rasterizer ──coverage──► Canvas ──► IImageEncoder ─► byte[]
              ▲
         IFontSource (SixLaborsFontSource + OutlineCollector)
```

### Layout details
- `scale = FontSize / UnitsPerEm`
- Iterate **code points** with `text.EnumerateRunes()`, not `char`s
- `penX` starts at `Padding`; after each glyph `penX += AdvanceWidth · scale`
- `Width = ceil(totalAdvance) + 2·Padding`
- `Height = ceil((Ascender − Descender) · scale) + 2·Padding`
- `Baseline = Padding + Ascender · scale`

### Rasterizer details
- For each pixel row, for each of 4 sub-scanlines (`y + (i + 0.5)/4`):
  - intersect all edges with the sub-scanline and record `(x, ±1 winding)`
  - sort by x and walk the crossings with a running winding count; where it is non-zero, accumulate coverage over 4 horizontal sub-samples per pixel
- Coverage = hits / 16, clamped to [0, 1]
- Only iterate rows/columns inside the glyph's edge bounding box, clipped to the canvas

## Edge cases

| Case | Behaviour |
|---|---|
| Space / whitespace | Has an advance width but no contours: move the pen, draw nothing |
| Missing character | Use glyph 0 (`.notdef`), don't throw |
| Empty string | Return a padding-only canvas (at least 1×1), not 0×0 |
| Surrogate pairs / emoji | Handled via `EnumerateRunes()`; renders `.notdef` if the font lacks the glyph |
| Newline | Out of scope; treated like any other code point (renders `.notdef` or nothing) |
| Glyph overlapping canvas edge | Clip in the rasterizer |

## Build order

**Rule:** each step adds its unit tests, and `dotnet test` must be green before starting the next step. Tests stay small: hand-built inputs, exact or near-exact expected values. Step 0 sets up the test project so this works from the start.

0. **Test project**
   - Create `TextRenderer.Tests` (xUnit), reference `TextRenderer`, add to `TextRenderer.sln`
   - Tests locate `fonts/` relative to the repo root (a small `TestPaths` helper)
   - ✅ `dotnet test` runs one trivial passing test.
1. **Spike the font source**
   - `dotnet add TextRenderer package SixLabors.Fonts`
   - Implement `IFontSource`, `SixLaborsFontSource`, `OutlineCollector`, `FontMetrics`, `Glyph`, `Contour`
   - Confirm coordinates are in font units (roughly 0..`UnitsPerEm`) and Y points up. If SixLabors flips Y, un-flip it in `OutlineCollector`. To get 1:1 font units, render at `pointSize = UnitsPerEm`, 72 DPI.
   - **Spike findings (SixLabors.Fonts 3.2.0):**
     - `Glyph.RenderTo` is internal. Use `SixLabors.Fonts.Rendering.TextRenderer.RenderTo(renderer, glyphId, GlyphOptions)`, which renders one glyph by ID with no text layout.
     - With `TextBaseline = Alphabetic` and `Origin = 0`, the origin is the baseline, but Y is **down**. `OutlineCollector` negates Y.
     - Glyph IDs come from `Font.TryGetGlyphId`, falling back to 0 (`.notdef`). Metrics come from `FontMetrics.TryGetGlyphMetrics(glyphId, …)`.
     - DejaVuSans: `UnitsPerEm` 2048, ascender 1901, descender −483. "H" advance is 1540, lsb 201.
     - ⚠️ 3.x needs a Six Labors licence key for **Release** builds. Debug builds only give a warning.
   - 🧪 Tests:
     - DejaVuSans metrics: `UnitsPerEm == 2048`, `Ascender > 0`, `Descender < 0`
     - "H": one contour, positive advance, all points within `0..UnitsPerEm`, top of the H has larger Y than the bottom (Y-up)
     - "o": two contours
     - Space: positive advance, zero contours
     - Unmapped code point (e.g. U+E000): returns `.notdef`, doesn't throw
2. **Canvas + PPM**
   - `Canvas`, `IImageEncoder`, `PpmEncoder`
   - 🧪 Tests:
     - `Clear` sets every pixel; `Blend` with coverage 0 / 0.5 / 1 gives background / midpoint / foreground
     - Blending the same pixel twice at 0.5 gives 75%, not 100% ("over", not additive)
     - Out-of-bounds `Blend` is ignored
     - PPM output starts with `P6\n{w} {h}\n255\n` and has length `header + w·h·3`
   - 👀 Write a filled rectangle to `out/test.ppm` and open it.
3. **Flatten + rasterize (hard edges)**
   - `Edge`, `PathFlattener` (lines first, then quad/cubic subdivision), `Rasterizer` with 1 sample per pixel
   - 🧪 Tests:
     - Flattener: scale 0.5, baseline 10 maps font point `(4, 8)` to `(penX + 2, 6)` (Y-flip); line contour gives one edge per segment; curve start and end points are preserved; quad/cubic produce N edges
     - Rasterizer: axis-aligned square `(2,2)–(6,6)` fills exactly those 16 pixels and nothing else
     - Square with a reversed inner square leaves the inner pixels empty (non-zero winding)
     - Two overlapping same-direction squares don't leave a hole
     - Shape partly off-canvas doesn't throw and is clipped
   - 👀 Render a single "H", then an "o".
4. **Layout + orchestrator**
   - `RenderOptions`, `TextLayout`, `RenderPipeline`
   - 🧪 Tests (using a fake `IFontSource` with known advances, so expected values are exact):
     - Pen positions are cumulative scaled advances starting at `Padding`
     - Width/height/baseline match the formulas above
     - Spaces advance the pen; empty string gives a padding-only canvas ≥ 1×1
     - Surrogate pair counts as one glyph
   - 👀 Render "Hello, World!" in all three DejaVu fonts.
5. **Anti-aliasing**
   - Switch the rasterizer to 4×4 supersampling with coverage-based "over" blending
   - 🧪 Tests:
     - Integer-aligned square still gives exactly coverage 1 inside, 0 outside
     - Square edge at x = 2.5 gives 0.5 coverage in that column
     - All coverage values are within [0, 1]
   - 👀 Edges look smooth when zoomed in.
6. **PNG encoder**
   - `PngEncoder` (IHDR / IDAT with zlib / IEND, CRC32)
   - 🧪 Tests:
     - Output starts with the 8-byte PNG signature; IHDR width/height match
     - CRC of `IEND` matches the known value `AE 42 60 82`
     - Round-trip: decode IDAT with `ZLibStream`, strip filter bytes, get the original pixels back
   - 👀 The output opens in a browser.
7. **Image correctness tests**: invariants, golden images, then the oracle test. See the next section.

## Testing the final image

Checking by eye (👀) is necessary, because only a person can tell whether text looks right. But it isn't enough on its own: it's slow, you can't repeat it on every change, and it misses small errors such as being 1px off or one glyph slightly wrong. So we check by eye **once**, to approve an image, and use automated checks from then on.

Three layers, cheapest first:

### 1. Invariant tests (no reference images)
Facts that must hold for any correct render, computed from the font data itself:

- **Ink area ≈ outline area.** Total coverage summed over the canvas should equal the scaled area of the flattened contours (shoelace formula, holes subtracting via winding). Tolerance about 2%. This is the strongest single check of the rasterizer.
- **Ink bounding box ≈ glyph bounding box.** For a single glyph, the box of pixels with coverage > 0 should match the scaled outline bounds, within 1px.
- **Holes are empty.** The pixel at the centre of "o" and "O" is background.
- **Baseline.** "x" has no ink below the baseline; "p" and "g" do.
- **Spacing.** Rendering "HH" puts the second H's ink exactly `advance · scale` to the right of the first.
- **Whitespace.** " " renders no ink at all.
- **Deterministic.** The same input renders byte-identical output twice.

### 2. Golden-image (snapshot) tests
- A small set of inputs: "Hello, World!", "Hgjpqy" (descenders), "Oo@%&" (holes), "iiilll" (thin glyphs), at 12px and 48px, in DejaVuSans and DejaVuSerif.
- First run writes the PNG to `TextRenderer.Tests/golden/`. You check it by eye once, then commit it.
- Later runs compare the canvas pixels to the golden image. Allow a small tolerance (e.g. max per-pixel difference ≤ 2 and < 0.1% of pixels differing) so tiny float changes don't fail the test.
- On failure, write `actual.png` and `diff.png` next to the golden image so you can see what changed.
- This catches **regressions**. It only proves correctness as far as your one eyeball check did.

### 3. Oracle test: comparison against a trusted renderer (required, last step of Job 1)
- In the **test project only**, render the same text with an independent renderer (e.g. `SixLabors.ImageSharp.Drawing` or SkiaSharp) using the same font, size and baseline.
- Compare coverage masks with a tolerance: intersection-over-union of inked pixels > 0.95, or mean absolute difference below a threshold. Anti-aliasing differs between renderers, so exact matches aren't expected.
- This is the closest thing to an objective "is it right" check. It's allowed because the rule against renderer libraries applies to the production code, not to tests that check it.

- Prefer **SkiaSharp** as the oracle: it has its own font parser and rasterizer, so it shares no code with ours. ImageSharp.Drawing would reuse the SixLabors font parsing, which makes it a weaker check.

**Priority:** layer 1 gives the most confidence for the least effort. Then layer 2, so the eyeball check is recorded and repeatable. Layer 3 (the oracle) closes out Job 1.

## How to run it before Job 2

Temporary CLI mode in `Program.cs`, checked before the web host starts:

```
dotnet run --project TextRenderer -- render "Hello" fonts/DejaVuSans.ttf 48 out/hello.png
```

This gets replaced or joined by the HTTP endpoint in Job 2.

## Out of scope for Job 1

- Kerning, ligatures, complex shaping (Arabic, Indic), right-to-left text
- Multi-line text and word wrap
- Hinting and subpixel (LCD) rendering
- Glyph caching (Job 2; enabled by decision 2)
