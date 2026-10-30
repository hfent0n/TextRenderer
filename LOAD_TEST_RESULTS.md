# Load Test Results

Run on 2026-10-30 with `TextRenderer.LoadTests` (NBomber 6.7.1). The harness is not part of `dotnet test`.

```bash
dotnet run --project TextRenderer.LoadTests -- http://localhost:5133
```

**Setup:** 10-core Mac on mains power, lid open, no sleep during the runs. Load generator and service on the same machine. Service run as a **Debug** build (Part 2, decision 8) in `Production` mode, with logs discarded and default limits (10 concurrent renders, queue of 100, 5 s timeout).

## Results

| Scenario | Clients | Seconds | OK/s | p50 ms | p95 ms | p99 ms | Max ms | OK | Other responses |
|---|---|---|---|---|---|---|---|---|---|
| Typical: 20 characters, 32px | 10 | 60 | 2,415 | 3.7 | 6.4 | 8.5 | 89 | 144,912 | none |
| Large: 41 characters, 512px | 4 | 60 | 8.3 | 477 | 500 | 579 | 606 | 498 | none |
| Mixed: 80% typical, 15% medium, 5% large | 20 | 60 | 270 | 39 | 261 | 673 | 1,061 | 16,172 | none |
| Overload: typical | 400 | 30 | 1,881 | 66 | 138 | 233 | 334 | 56,438 | 713,291 `429` |
| Soak: mixed | 20 | 300 | 259 | 41 | 321 | 688 | 1,250 | 77,792 | none |

Latency columns cover successful responses only.

Across about 1 million requests there were **no `5xx` responses and no timeouts**.

## Findings

- **Overload is handled as designed.** With 40× more clients than slots, the service kept rendering at 1,881/s, 78% of its normal rate, and shed the excess as `429`s at about 23,800/s. p99 for accepted requests stayed at 233 ms.
- **Memory is stable.** During the soak it moved between about 850 MB and 1.1 GB with no upward trend. The peak across the whole run was 1.5 GB. Memory is high because large renders allocate large canvases (a 7-megapixel image is about 28 MB of RGBA) and .NET holds on to freed memory while it has room. The overload run, with small images only, peaked at about 510 MB.
- **Large renders dominate cost.** One takes about 480 ms against about 4 ms for a typical label. At 5% of the traffic, they cut throughput from 2,415/s to about 270/s and set the mixed p95 and p99. If callers really send images this large, lowering `MaxCanvasPixels` is the simplest control.

## Limits of these numbers

- **Debug build:** Release would likely be faster, so treat these as a conservative baseline. SixLabors.Fonts 3.x needs a licence key for Release builds.
- **Same machine:** the load generator competes with the service for CPU.
- **Five minutes is short:** a slow leak could need a longer soak to show up.
- **Overload `429` latency wasn't measured**, only that of accepted requests.
- **Mixed percentiles are combined** across request sizes, so they don't show how long a typical request waits behind large ones.
- **Single run:** no repeat runs, so run-to-run variation is unknown.

## Harness note

NBomber stops a scenario after 5,000 failures by default and counts `429`s as failures, so the first overload run stopped after about 1 second. The harness now sets `WithMaxFailCount(int.MaxValue)`, and the overload row above comes from a rerun of that scenario for the full 30 seconds.
