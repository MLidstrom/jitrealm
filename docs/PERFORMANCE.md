# Performance notes (driver)

This repo includes a small, repeatable driver benchmark harness to measure **CPU costs** of:

- Heartbeat scheduling + dispatch
- Callout scheduling + dispatch
- Object cloning overhead (load/clone loop)

The intent is to support before/after comparisons when making driver optimizations.

## Running the benchmark

From repo root:

```bash
dotnet run --project .\JitRealm.csproj -c Release -- --perfbench --blueprint std/perf_dummy.cs --count 2000 --ticks 5000 --loopDelayMs 50
```

Common flags:

- `--count N`: number of instances to clone
- `--ticks N`: number of simulated loop ticks
- `--loopDelayMs N`: tick duration (simulated time advances by this each tick)
- `--noCallouts`: disable scheduling callouts (heartbeat-only baseline)
- `--safeInvoke`: wrap heartbeat/callout invocations with `SafeInvoker` (adds overhead, but matches production safety behavior)

## Baselines

These numbers are meant as **relative baselines** (they vary by machine and build).

### Heartbeat-only (callouts off)

Command:

```bash
dotnet run --project .\JitRealm.csproj -c Release -- --perfbench --blueprint std/perf_dummy.cs --count 2000 --ticks 5000 --loopDelayMs 50 --noCallouts
```

Result (Dec 31, 2025):

- Created 2000 instances in ~67.5 ms
- Heartbeat scheduler: ~41.3 ms (due total 500,000)

### Callouts on (includes reflection dispatch)

Command:

```bash
dotnet run --project .\JitRealm.csproj -c Release -- --perfbench --blueprint std/perf_dummy.cs --count 2000 --ticks 5000 --loopDelayMs 50
```

Result (Dec 31, 2025):

- Created 2000 instances in ~65.2 ms
- Heartbeat scheduler: ~47.3 ms (due total 500,000)
- Callout scheduler: ~229.0 ms (due total 500,000)


