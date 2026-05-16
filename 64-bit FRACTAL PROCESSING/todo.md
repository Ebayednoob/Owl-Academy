# todo.md — Further Work & Next Steps

> **Repository:** `ccpV1/` — SHD-CCP Protocol + DevLab + RevProto v2.0  
> **Status:** Active — items are not prioritized for completion order; see §1 for priority tiers  
> **Last updated:** 2026-05-15

---

## Contents

1. [Priority Tiers](#1-priority-tiers)
2. [Phase 1 — Vertical Scaling (SIMD, no infra)](#2-phase-1--vertical-scaling-simd-no-infra)
3. [Phase 2 — Horizontal Scaling (Workers + Scheduler + WAL)](#3-phase-2--horizontal-scaling-workers--scheduler--wal)
4. [Phase 3 — GPU Offload](#4-phase-3--gpu-offload)
5. [Verification & Testing Gaps](#5-verification--testing-gaps)
6. [Infrastructure & Ops](#6-infrastructure--ops)
7. [GUI & DevLab Polish](#7-gui--devlab-polish)
8. [RevProto v2.0 Oddments](#8-revproto-v20-oddments)
9. [Long-term / Open Research](#9-long-term--open-research)

---

## 1. Priority Tiers

| Tier | Meaning | Typical effort |
|---|---|---|
| **P1** | Bug fix / correctness — deliver now | < 1 day |
| **P2** | Feature that unlocks measurable throughput gain | 1–5 days |
| **P3** | Infrastructure that unblocks multiple downstream items | 1–2 weeks |
| **P4** | Research / experiment — uncertain outcome | ≥ 1 week |

---

## 2. Phase 1 — Vertical Scaling (SIMD, no infra)

The goal is to prove that SIMD-vectorizing the hot inner loops in the SHD-CCP C#
core delivers measurable latency reduction before any worker-node or GPU work begins.

### P2 — Vectorize trefoil knot sampling

**File:** `src/QuaternionKnot.cs`  
Use `System.Numerics.Vector256<float>` or `Vector128<float>` to evaluate 4–8
`t` values in a single instruction.

```csharp
// scalar (current)
for (int i = 0; i < n; i++) {
    float t = (float)i / n * 2 * MathF.PI;
    points[i] = new Vector3(sin(t) + 2*sin(2*t), ...);
}

// SIMD target
Vector256<float> tBase = ...; // [t0, t1, t2, t3]
Vector256<float> sinT  = SimdSin(tBase);
Vector256<float> sin2T = SimdSin(2 * tBase);
// one instruction rather than 4 scalar calls
```

**Deliverable:** `ExperimentSIMD_Quaternions.cs` in `simulation_src/` (or a new `experiments/` folder).  
**Acceptance:** ≥ 25% throughput improvement on 1 000-point batch measured in `Benchmark.cs`.  
**Effort:** 1–2 days.

---

### P2 — Batch CRC folding

**File:** `src/BitstreamSerializer.cs`  
Current code calls `CalculateCRC32(byte[])` once per packet. For multi-shard
assemblies, fold per-shard CRCs into a root CRC with a single accumulator pass
instead of serializing each shard's full byte array back through `CalculateCRC32`.

```csharp
// target pattern: per-shard CRC + CRC of concatenated CRC values = root CRC
uint rootCrc = CRC32.Merge(crcShard0, crcShard1, crcShard2);  // constant time
```

**Acceptance:** `Benchmark.cs` verifies `CRC32.Merge` produces identical output to
full-byte re-hash on 1 000 random shards.  
**Effort:** half-day.

---

### P2 — `Parallel.For` shape batch loop

**Files:** `src/SHDCcpProtocol.cs`, `examples/Program.cs`  
Wrap the per-knot conversion in `System.Threading.Tasks.Parallel.For` or
`Parallel.ForEach` at batch level. Each knot is independent; no locking needed.

```csharp
Parallel.For(0, batch.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
{
    _ = _protocol.ConvertShapeToProtocolPacket(batch[i], i);
});
```

**Measurement anchor:** add `Benchmark_BatchProcess.cs` cases for 100 / 1 000 / 10 000-shape batches.

**Acceptance:** 4-shape batch 4× faster on 4-core; speedup correlates with
`Environment.ProcessorCount - 1` within ±15%.  
**Effort:** half-day.

---

## 3. Phase 2 — Horizontal Scaling (Workers + Scheduler + WAL)

### P3 — `WorkerNode` TCP class

**New file:** `devlab/cli/WorkerNode.cs` (or `src/WorkerNode.cs` for dual-use)  
A stateless TCP listener that accepts shard-assignment packets from the scheduler,
runs the full SHD-CCP pipeline on its assigned slice `[t_start, t_end]`, and
returns `{ partialBitstream, localCRC }`. Workers are homogeneous — any worker
can handle any shard.

```csharp
// message format (flat buffer / MessagePack recommended)
struct WorkerAssignment {
    Guid   SessionId;
    int    ShardIndex;
    int    TotalShards;
    int    TessellationStart;   // point index (0-based)
    int    TessellationEnd;     // exclusive
    byte[] SessionKey;          // optional AES-GCM session key for RevProto
}
```

**Effort:** 2–3 days.

---

### P3 — Scheduler / Load-Balancer

**New file:** `devlab/cli/Scheduler.cs`  
REST + WebSocket ingest layer that:
1. Accepts incoming shape or payload submissions
2. Shards them across available workers (round-robin by default; hash by
   `sessionId` for sticky sessions; least-conn for dynamic clusters)
3. Tracks ACKs and reschedules failed/dropped shards
4. Returns assembled bitstream to caller when all shards are done

**Acceptance:** `devlab/tests/test_scheduler.py` verifies zero data loss on worker
kill mid-shard (WAL replay rescue path exercised).  
**Effort:** 2–3 days.

---

### P3 — Bitstream merge + CRC tree

**File:** `src/BitstreamSerializer.cs`  
Implement Option B from the concept doc: the root worker computes a CRC fold of
the per-shard CRCs rather than re-serializing every shard.

```csharp
// Root CRC = CRC32 of concatenated CRC values of each shard
static uint MergePartialCrCs(ReadOnlySpan<uint> shardCRCs);
```

**Effort:** 1–2 days.

---

### P3 — WAL + replay

**New file:** `devlab/cli/WalWriter.cs` (append-only JSON lines file)  
Every shard assignment, partial result, and ACK is written before delivery.
On startup the scheduler replays the WAL to recover in-flight sessions. Desired
properties:

| Property | Target |
|---|---|
| Durability | fsync after each write |
| Recovery time | < 5 s for 100 k-entry WAL |
| File size | < 1 MB per 10 k entries |

**Effort:** 2 days.

---

## 4. Phase 3 — GPU Offload

### P4 — AES-NI benchmark harness

**New file:** `src/Benchmarks/AesNiBenchmark.cs`  
Use `System.Runtime.Intrinsics.X86.Aes` and `System.Runtime.Intrinsics.X86.Sse2`
to exercise the hardware AES engine and measure real GB/s throughput on the
target machine.

```csharp
using System.Runtime.Intrinsics.X86;

if (Aes.IsSupported)
{
    // encrypt 1 KB, 1 MB, 1 GB blocks; report cycles/byte
}
```

**Benchmark baseline needed before any claim of "AES-NI sweet spot" can be made.**

**Effort:** 2–3 days.

---

### P4 — CUDA trefoil sampler kernel

**New file:** `devlab/gpu/trefoil_sampler.cu` + C# interop shim  
A CUDA kernel that passes `t` values to `sin()` / `cos()` on the GPU using
`__device__` math functions and writes results to a pinned `float3` buffer readable
from C# via `cudaMemcpy`.

```cuda
__global__ void TrefoilSamplerKernel(float3* outPoints, int n, float scale) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    float t = (float)i / n * 2.0f * M_PI;
    outPoints[i] = make_float3(
        sinf(t) + 2.0f * sinf(2.0f*t),
        cosf(t) - 2.0f * cosf(2.0f*t),
       -sinf(3.0f*t)
    );
}
```

**Effort:** 5–7 days. Requires CUDA toolkit, CUDA-capable GPU, and C# interop
bindings (`CudaInterop.cs`).

---

### P4 — WebGPU visualizer in DevLab GUI

**New file:** `ccpV1/devlab/gui/src/components/WebGPUKnotViewer.tsx`  
Replace or augment the `Canvas2D/WebGL` fallback viewer with a WebGPU compute
shader that evaluates the trefoil parametric equation on the GPU and renders
directly via `requestAdapter("webgpu")`.

**Effort:** 3–5 days.

---

## 5. Verification & Testing Gaps

| Item | Why | Effort |
|---|---|---|
| **Property-based test** for quaternion normalization | Verify `|q|` stays 1.0 ± 1e-6 after every shard merge; currently only round-trip accuracy is measured | 1 day |
| **Fuzz feed into ProtocolState machine** | `SHDCcpProtocol.cs` state machine should survive arbitrary byte arrays without assert/crash | 1 day |
| **ChaCha20-Poly1305 benchmark in `Benchmark.cs`** | Concept doc only benchmarks AES-256-GCM; RevProto spec mandates both ciphers as options | 1 day |
| **End-to-end cluster smoke test** | Spin up 2 worker containers, run 100-shape batch, verify round-trip 100% | 2 days |
| **Wireshark dissector update for multi-shard packets** | `wireshark/myproto.lua` only covers single-packet primary stream; add shard-header and CRC-tree framing | 1 day |
| **GCM tag-failure rate monitoring** | Alert in DevLab GUI if any GCM tag ever mismatches on decrypt — must distinguish between noise and real attack | 2 days |

---

## 6. Infrastructure & Ops

| Item | Notes | Effort |
|---|---|---|
| **Remove `ccpV1/ccpV1/publish/` nested duplicate** | Appears to be an artifact of `scripts/build.sh` copy step; confirm with build team before deleting | 1 hour |
| **Add CI/CD pipeline** (GitHub Actions / GitLab CI) | Run `dotnet test`, `tsc --noEmit`, `npm run build` on every push; gate merges on green | 2 days |
| **Dockerize workers + scheduler** | Dockerfile per worker; `docker-compose.yml` for 1-scheduler + 3-worker local cluster; services communicate over a bridge network | 2 days |
| **Prometheus + InfluxDB observability stack** | Wire the gauge/counter metrics from the concept doc's §10 into a scrapeable format; DevLab GUI reads the TSDB for dashboards | 3–5 days |
| **10 GbE NIC + switch procurement plan** | Needed before Scenario C (cluster) is achievable; document lead-time and budget | ½ day |

---

## 7. GUI & DevLab Polish

| Item | Current state | Target |
|---|---|---|
| **PacketBuilder preset state preserved across reloads** | Presets are `Date.now()` values re-evaluated on every mount; should persist to `localStorage` | low-changing UX |
| **MetricsGrid last-updated timestamp** | Metrics refresh on WebSocket push only; add a `lastUpdated` field to `BridgeState` and show "Updated X s ago" | small P2 |
| **ConsoleOutput filter (info / warn / err toggle)** | All logs flat; add a three-way filter header to the terminal panel | P2 |
| **Dashboard resize breakpoints** | `lg:grid-cols-12` layout is fixed; add `md:` breakpoint for tablet, test on iOS Safari | P3 |
| **Bridge reconnection UX** | When `status` is `disconnected`, show "Reconnecting in N s…" countdown instead of static label | P2 |
| **Add TTS voice selector** | Presentation deck uses default OS voice; let the user pick from `speechSynthesis.getVoices()` | P3 |

---

## 8. RevProto v2.0 Oddments

The Python prototype (`ccpV1/prototype/`) is functional but has two tracked gaps
in `INTEGRATION_REPORT.md`. They remain ⚠ experimental.

| Item | Description | Priority |
|---|---|---|
| **Stego channel embed speed** | LSB/timing channels currently sequential; add `concurrent.futures.ThreadPoolExecutor` overlay | P2 |
| **ChaCha20-Poly1305 AEAD expiry ordering** | Prototype calls `encrypt_and_digest()` — verify explicit `encrypt-then-MAC` ordering in `revolutionary_protocol.py` before any production-style deployment | P1 (correctness) |
| **Angry IP Scanner probe template** | `angryip/probe_config.ips` is present but not wired to any CLI command; at minimum add `--scan` list parse in `run_prototype.py` | P3 |
| **x64dbg analysis template** | `x64dbg/analysis_template.py` is a skeleton; add function-name extraction and memory-map snapshot | P3 |

---

## 9. Long-term / Open Research

These items are marked as "open questions" in `CONCEPT_SCALING_COMPUTING_POWER.md`
and require experimental validation before a decision can be made.

| Question | Experiment | Owner |
|---|---|---|
| Does the emergent 4th phase property hold at scaled-out shard counts? | Run 100 000-point batched shape, compare phase-3 recombination error at 1 / 4 / 16 workers | SHD-CCP Agent |
| Does CRC tree-fold produce identical output to periodic reconvergence at high shard counts? | `Benchmark.cs`: compare CRC output for 10 / 100 / 1 000 shards | SHD-CCP Agent |
| What is the ChaCha20 vs AES-256-GCM throughput gap on AMD Zen 4 vs Intel Alder Lake? | `Benchmarks/AesNiBenchmark.cs` extended to both ISAs | infra team |
| Does stego channel ordering affect detectable-entropy? | Measure KL-divergence of embedded carrier distributions for all 3! orderings | paper lead |
| Can `hkdf_expand_label` be amortized across a batch of session keys rather than per-packet? | Profile `run_prototype.py --capture` with 1 000 packets; measure HKDF call fraction | proxy lead |

---

*This file tracks only work that has not yet been completed. Items marked ✅ in `done.md`
are intentionally omitted here. When an item is finished, move it from `todo.md` to
`done.md` and update the commit summary.*
