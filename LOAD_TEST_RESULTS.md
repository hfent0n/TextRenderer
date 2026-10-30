# Load Test Results

Run on 2026-10-30 with `TextRenderer.LoadTests` (NBomber 6.7.1), after the cleanup and with response caching in place. The harness is not part of `dotnet test`.

```bash
dotnet run --project TextRenderer.LoadTests -- http://localhost:5133
```

**Setup:** 10-core Mac on mains power, lid open, no sleep during the runs. Load generator and service on the same machine. Service run as a **Debug** build (Part 2, decision 8) in `Production` mode, with logs discarded and default limits (10 concurrent renders, queue of 100, 5 s timeout).

Every scenario repeats the same few requests, so it was run two ways.

## Rendering every request (cache effectively off)

Run with `Render__MaxCacheBytes=1`. This measures the renderer itself, and the worst case where no request repeats.

| Scenario | Clients | Seconds | OK/s | p50 ms | p95 ms | p99 ms | Max ms | OK | Other responses |
|---|---|---|---|---|---|---|---|---|---|
| Typical: 20 characters, 32px | 10 | 60 | 2,390 | 3.7 | 6.5 | 9.3 | 110 | 143,388 | none |
| Large: 41 characters, 512px | 4 | 60 | 8.3 | 478 | 501 | 514 | 597 | 498 | none |
| Mixed: 80% typical, 15% medium, 5% large | 20 | 60 | 263 | 40 | 311 | 691 | 1,006 | 15,774 | none |
| Overload: typical | 400 | 30 | 1,741 | 74 | 118 | 168 | 293 | 52,218 | 891,330 `429` |
| Soak: mixed | 20 | 300 | 256 | 41 | 551 | 682 | 1,021 | 76,678 | none |

## With the cache

Default settings. After the first render of each request, nearly everything is a cache hit, so this is the **best case**.

| Scenario | Clients | Seconds | OK/s | p50 ms | p95 ms | p99 ms | Max ms | OK | Other responses |
|---|---|---|---|---|---|---|---|---|---|
| Typical | 10 | 60 | 46,166 | 0.2 | 0.3 | 1.2 | 99 | 2,769,979 | none |
| Large | 4 | 60 | 6,995 | 0.4 | 1.0 | 2.9 | 647 | 419,698 | none |
| Mixed | 20 | 60 | 34,769 | 0.4 | 1.4 | 2.6 | 38 | 2,086,159 | none |
| Overload | 400 | 30 | 45,264 | 8.3 | 12 | 15 | 74 | 1,357,922 | none |
| Soak | 20 | 300 | 31,751 | 0.4 | 1.6 | 3.3 | 44 | 9,525,432 | none |

Latency columns cover successful responses only. Across about 17.3 million requests in the two runs there were **no `5xx` responses and no timeouts**.

## Findings

- **Overload is handled as designed.** Without the cache, 40× more clients than slots still rendered 1,741/s, 73% of the normal rate, and shed the excess as `429`s at about 29,700/s, with p99 for accepted requests at 168 ms. With the cache, the same load needed no render slots, so there were no `429`s at all.
- **Memory is stable.** Without the cache, memory averaged about 1.0 GB in both halves of the soak, ranging from 630 MB to 1.3 GB. With the cache, it held at about 155 MB, peaking at 398 MB, because almost no canvases are allocated.
- **Large renders dominate cost.** One takes about 480 ms against about 4 ms for a typical label. At 5% of the traffic, they cut throughput from 2,390/s to about 260/s. Lowering `MaxCacheBytes` won't help this; lowering `MaxCanvasPixels` would.
- **The cache's value depends on how often requests repeat.** Repeats were up to about 840× faster (large: 8.3/s to 6,995/s), but a service whose requests rarely repeat should expect the first table instead. The real hit rate is unknown, and `textrenderer.render.cache.hits` would show it.

## Compared with before the cleanup

| Scenario | Before OK/s | After OK/s | Before p99 ms | After p99 ms |
|---|---|---|---|---|
| Typical | 2,415 | 2,390 | 8.5 | 9.3 |
| Large | 8.3 | 8.3 | 579 | 514 |
| Mixed | 270 | 263 | 673 | 691 |
| Overload | 1,881 | 1,741 | 233 | 168 |
| Soak | 259 | 256 | 688 | 682 |

Throughput moved by 3% or less, except overload at −7%. With single runs, these differences aren't clearly meaningful. The cleanup didn't cause a visible slowdown, and laying text out once didn't give a visible speed-up.

The soak's p95 rose from 321 ms to 551 ms, but p95 sits right at the 5% share of large requests, so it swings easily between runs.

## Limits of these numbers

- **Debug build:** Release would likely be faster, so treat the rendering numbers as a conservative baseline. SixLabors.Fonts 3.x needs a licence key for Release builds.
- **Same machine:** the load generator competes with the service for CPU, and may itself be the limit in the cached run.
- **Five minutes is short:** a slow leak could need a longer soak to show up.
- **Overload `429` latency wasn't measured**, only that of accepted requests.
- **Mixed percentiles are combined** across request sizes, so they don't show how long a typical request waits behind large ones.
- **Single runs:** run-to-run variation hasn't been measured.

## Harness note

NBomber stops a scenario after 5,000 failures by default and counts `429`s as failures, so the harness sets `WithMaxFailCount(int.MaxValue)`.
