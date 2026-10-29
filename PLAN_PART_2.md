# Job 2 Plan — Text Rendering Service

Goal: expose the Job 1 renderer over HTTP so internal teams can call it heavily and rely on it. Deployment is out of scope: the process running locally is the service.

Job 1 (rendering core, its decisions and its tests) is in [PLAN_PART_1.md](PLAN_PART_1.md). The same working rules apply: each step adds its tests, `dotnet test` is green before the next step starts, and any decision not written here is raised before it's made.

## Decisions

1. **Fonts come from a server-side registry, not uploads.** All fonts in `fonts/` are loaded once at startup and chosen by ID in each request. Uploaded fonts would mean parsing untrusted binaries on every request: a security risk, slow, and impossible to cache.
2. **Unsupported characters are rejected, not rendered as `.notdef`.** A request containing any character the chosen font has no glyph for, or any control character, gets `400` listing the offending characters. Callers find out immediately rather than shipping images with boxes in them.
3. **Limits are configurable** through `appsettings.json` (section `Render`), with safe defaults.
4. **Errors use `ProblemDetails`** (RFC 7807) everywhere, with no stack traces in responses.
5. **Output format is chosen by a `format` field** (`png` default, or `ppm`), using the existing `IImageEncoder` implementations.
6. **The canvas is sized to fit the ink** as well as the advances, so glyphs that overhang their advance (e.g. serif "Å" at `Padding = 0`) are never clipped.
7. **Rendering is bounded**: a concurrency limiter caps simultaneous renders, and a timeout cancels slow ones.

8. **Stay on SixLabors.Fonts 3.2 and run the service as a Debug build.** A downgrade was tried and rejected:
   - 2.2.0 has the same Release-build licence check as 3.x.
   - 2.1.3 has no build-time check, but it's under the **same Six Labors Split License**: it only doesn't enforce it. Its public API also has no per-glyph outline access, so outlines would have to come from SixLabors' own text renderer one character at a time, with its internal line-box offset undone by hand. That's fragile, and it bends Part 1 decision 1.
   - Since there's no deployment, Debug is fine. The write-up should state the licence position. If the service ever ships, either buy a licence or swap `SixLaborsFontSource` for an MIT-licensed parser. `IFontSource` keeps that swap to one class.

## API

### `POST /render`

Request (`application/json`):

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `text` | string | yes | | Single line. Every character must exist in the font; no control characters |
| `font` | string | yes | | Font ID from `GET /fonts`, e.g. `dejavu-sans` |
| `fontSize` | number | no | 48 | Pixels per em, within limits |
| `padding` | integer | no | 8 | Pixels, within limits |
| `foreground` | string | no | `#000000` | `#RRGGBB` |
| `background` | string | no | `#FFFFFF` | `#RRGGBB` |
| `format` | string | no | `png` | `png` or `ppm` |

Responses:

| Status | When | Body |
|---|---|---|
| `200` | Rendered | Image bytes, `Content-Type: image/png` or `image/x-portable-pixmap` |
| `400` | Malformed JSON, missing/invalid field, unknown font, unsupported characters, limit exceeded | `ProblemDetails` with an `errors` map per field |
| `413` | Body larger than the limit | `ProblemDetails` |
| `429` | Too many renders queued | `ProblemDetails`, `Retry-After` header |
| `503` | Render exceeded the timeout | `ProblemDetails` |
| `500` | Unexpected error | `ProblemDetails` without internals; full details in the log |

Example unsupported-character error:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "text": ["Font 'dejavu-sans' has no glyph for U+1F600 '😀'.", "Control character U+000A is not allowed."]
  }
}
```

### `GET /fonts`

```json
[
  { "id": "dejavu-sans", "family": "DejaVu Sans", "unitsPerEm": 2048 },
  { "id": "dejavu-sans-mono", "family": "DejaVu Sans Mono", "unitsPerEm": 2048 },
  { "id": "dejavu-serif", "family": "DejaVu Serif", "unitsPerEm": 2048 }
]
```

IDs come from the file name, lower-case and hyphenated (`DejaVuSansMono.ttf` → `dejavu-sans-mono`).

### `GET /health`

`200 Healthy` once fonts are loaded. Uses ASP.NET's built-in health checks.

### OpenAPI

Keep the template's `AddOpenApi()` and describe the real endpoints, so teams can discover the contract.

## Limits

Bound from `appsettings.json` → `Render` into a `RenderLimits` options class. Defaults:

| Setting | Default | Why |
|---|---|---|
| `MaxTextLength` | 500 code points | Labels and headings, not documents |
| `MinFontSize` / `MaxFontSize` | 1 / 512 px | |
| `MaxPadding` | 256 px | |
| `MaxCanvasPixels` | 16,000,000 (e.g. 4000×4000) | **The limit that matters.** Catches combinations the others each allow (500 characters at 512px would be ~90 megapixels). 16 MP of RGBA is 64 MB |
| `MaxRequestBodyBytes` | 16 KB | |
| `RenderTimeout` | 5 s | |
| `MaxConcurrentRenders` | number of CPU cores | Rendering is CPU-bound |
| `MaxQueuedRenders` | 100 | Beyond this, `429` |
| `FontDirectory` | `fonts` | |

The canvas size check runs **after layout and before the canvas is allocated**, so an oversized request costs almost nothing.

## Validation rules

Checked in this order, collecting all field errors before responding:

1. Body is valid JSON and within `MaxRequestBodyBytes`.
2. `font` is present and is a known ID.
3. `text` is present, non-empty, at most `MaxTextLength` code points.
4. Every code point in `text` is a valid Unicode scalar value, is not a control character (category `Cc`), and exists in the font (`IFontSource.HasGlyph`). Report each offending character once, with its code point.
5. `fontSize`, `padding` are within limits; `foreground` / `background` parse as `#RRGGBB`; `format` is `png` or `ppm`.
6. After layout: `width × height ≤ MaxCanvasPixels`.

Space (U+0020) is allowed. Other whitespace such as tab or newline is a control character and rejected.

## Types

| Type | Kind | Responsibility |
|---|---|---|
| `RenderLimits` | options class | Bound from `appsettings.json` → `Render` |
| `IFontSource.HasGlyph(int)` | new interface method | Whether the font maps a code point (without falling back to `.notdef`) |
| `CachingFontSource` | `IFontSource` decorator | `ConcurrentDictionary<int, Glyph>` around another source. Glyphs are size-independent (Part 1, decision 2), so one cache per font serves every size |
| `FontRegistry` | singleton | Loads every font in `FontDirectory` at startup, wraps each in `CachingFontSource`, exposes `TryGet(id)` and the list for `GET /fonts` |
| `RenderRequest` | DTO | JSON request body (all fields nullable so missing fields can be reported) |
| `RenderRequestValidator` | class | Pure validation: request + registry + limits → `RenderOptions`, font and encoder, or a field→errors map. No HTTP, so it's unit-testable |
| `ColourParser` | static | `#RRGGBB` → `System.Drawing.Color` |
| `RenderPipeline` | existing | Gains a `CancellationToken`, checked between glyphs, for the timeout |
| `TextLayout` | existing | Canvas sized from ink bounds as well as advances (decision 6) |
| `Program.cs` | minimal API | Endpoints, DI, `ProblemDetails`, rate limiter, request timeouts, health checks, OpenAPI. Weather template removed |

## Build order

Must-have first. Everything from step 5 on is cut first if time runs out.

0. **Housekeeping**
   - Remove the weather-forecast template code.
   - 🧪 All Job 1 tests still pass.
1. **Font registry, `HasGlyph`, glyph cache, limits**
   - 🧪 Registry loads all three fonts with the expected IDs; unknown ID returns false.
   - 🧪 `HasGlyph` is true for 'A' and 'é', false for U+E000 and U+1F600.
   - 🧪 `CachingFontSource` returns the same `Glyph` instance on repeat calls and calls the inner source once per code point, including under parallel access.
   - 🧪 `RenderLimits` binds from configuration, and defaults apply when the section is missing.
2. **Request validation** (no HTTP)
   - 🧪 One test per rule: missing text/font, unknown font, too long, control characters, unsupported characters listed once each with code points, size/padding out of range, bad colours, bad format, oversized canvas.
   - 🧪 Multiple errors are all reported together.
   - 🧪 A valid request produces the expected `RenderOptions`, font and encoder.
3. **Endpoints and integration tests** (`WebApplicationFactory`, real HTTP pipeline in memory)
   - 🧪 `POST /render` returns `200 image/png`, and the bytes equal `PngEncoder` output from calling the pipeline directly.
   - 🧪 `format: "ppm"` returns `image/x-portable-pixmap`.
   - 🧪 Every validation failure returns `400 ProblemDetails` with the right field in `errors`.
   - 🧪 Malformed JSON gives `400`; oversized body gives `413`.
   - 🧪 `GET /fonts` lists the three fonts; `GET /health` returns `200`.
   - 🧪 A forced internal exception gives `500 ProblemDetails` without a stack trace.
4. **Protection: concurrency limit and timeout**
   - Rate limiter (concurrency policy) on `/render`; request timeout with cancellation passed into `RenderPipeline`.
   - 🧪 With `MaxConcurrentRenders = 1` and `MaxQueuedRenders = 0`, parallel requests get some `429`s with `Retry-After`.
   - 🧪 With a tiny `RenderTimeout`, a large render returns `503`, and the pipeline stops early (cancellation honoured between glyphs).
5. **Ink-bounds canvas sizing** (decision 6)
   - 🧪 Serif "Å" at `Padding = 0` has no ink in its clipped column (ink fully inside the canvas).
   - 🧪 Invariant: for every Job 1 sample, the ink bounding box lies within the canvas.
   - 🧪 Golden images are unchanged at the default padding (overhangs are smaller than 8px). If any change, review by eye and regenerate.
6. **Observability**
   - Structured log per request: font, size, text length (never the text itself), canvas size, render time, status.
   - Built-in ASP.NET metrics, plus a render-duration histogram.
   - 🧪 A render logs one entry with those fields (using a test logger).
7. **Concurrency and load testing**
   - 🧪 **Concurrency:** 200 parallel `POST /render` requests across all fonts and sizes return bytes identical to the same requests made one at a time. This checks SixLabors' `Font` and our cache are safe to share.
   - **Load test:** a separate NBomber console project (`TextRenderer.LoadTests`, not part of `dotnet test`). Scenarios: typical label (32px, ~20 characters), large render near the limits, and a mixed workload. Record throughput, p50/p95/p99 latency, error rate, and memory over a 5-minute soak in `LOAD_TEST_RESULTS.md`.
   - Expected finding to confirm: memory stays flat, and overload produces `429`s rather than rising latency.
8. **Could-have: response caching**
   - Rendering is deterministic, so identical requests can return a strong `ETag` (hash of the normalised request) and honour `If-None-Match` with `304`, and/or keep a small in-memory LRU of encoded images.
   - 🧪 Repeat request with `If-None-Match` returns `304` and no body.

## Testing summary

| Layer | Tool | Runs in `dotnet test` |
|---|---|---|
| Unit (registry, cache, validator, colours) | xUnit | Yes |
| Integration (HTTP contract, errors, limits) | xUnit + `WebApplicationFactory` | Yes |
| Concurrency (identical output under parallel load) | xUnit + `WebApplicationFactory` | Yes |
| Load and soak | NBomber console project | No, run by hand; results committed |
| Job 1 rendering correctness | Existing invariant, golden and oracle tests | Yes |

## Out of scope (deliberately)

- Deployment, Docker, CI, infrastructure (README).
- Authentication and per-team API keys. It's an internal service; this would be the first addition if it were exposed more widely.
- Uploaded fonts (decision 1).
- Multi-line text, word wrap, kerning, complex shaping.
- Distributed caching across instances.
- The CLI mode sketched in Part 1, which the HTTP endpoint replaces.

## Future work

- Small-text quality: exact-area coverage (see Part 1, "Future work").
- Streaming the encoded image straight to the response, and pooling canvas buffers with `ArrayPool`, to cut allocations under load.
- Per-client rate limits once callers are identified.
