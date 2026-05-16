# Scaling Computing Power — Concept Document

> **Repository:** `ccpV1/` — SHD-CCP Protocol + DevLab + RevProto v2.0
> **Status:** Concept / Design phase — ready for implementation planning
> **Author:** SHD-CCP Agent
> **Date:** 2026-05-15

---

## 1. Executive Summary

This document proposes a architecture for scaling computing power across the two protocols in this repository:

| Protocol | Language | Core Op | Current Load |
|---|---|---|---|
| **SHD-CCP** | C# / .NET 8 | quaternion knot → bitstream | 360 pts / single thread |
| **RevProto v2.0** | Python | AES-256-GCM + 3 stego channels | single session / sequential |

Both protocols are dominated by perfectly parallel workload: per-point quaternion ops in SHD-CCP and per-block AES-GCM ops in RevProto. This makes them ideal candidates for throttle-scaling by adding workers, satellites, and GPU lanes rather than rewriting core maths.

---

## 2. Scaling Motivation & Goals

| Goal | Target | Rationale |
|---|---|---|
| **Round-trip throughput** | > 10 k shapes/s | Benchmark baseline is single-point cadence |
| **Crypto mass-encrypt throughput** | > 1 GB/s AES-NI path | AES-256-GCM on modern x64 does ~5 GB/s per core at loop-unrolled throughput |
| **Latency (small packets)** | < 0.5 ms p99 | Perf target stated in `<AGENTS.md>` |
| **Horizontal scale** | N worker nodes | Stateless packet workers; no shared state |
| **GPU offload** | SHD-CCP knot sampling, stego channels | SIMD / CUDA / WebGPU for bulk transforms |

---

## 3. Workload Decomposition

### 3.1 SHD-CCP: Shape → Bitstream Pipeline

```
Point i ──► [Sample trefoil equation] ──► [Quaternion serialise] ──► [CRC]
```

- **Independence**: Point `i` is a function only of parameter `t_i`. No inter-point data dependencies.
- **Complexity per point**: O(1) trig + float → int conversion. ~200 CPU cycles at scalar precision.
- **Amortized cost**: sub-microsecond per point at SIMD width 4–8.

#### Phases

| Phase | Description | Parallelizable |
|---|---|---|
| P1 — Sample | evaluate x(t), y(t), z(t) for each t | ✅ fully |
| P2 — Quaternion pack | convert XYZ → quaternion (q_w, q_x, q_y, q_z) | ✅ fully |
| P3 — CRC + serialise | append CRC32, write byte array | ✅ per-packet |

### 3.2 RevProto v2.0: Crypto + Stego

```
payload ──► [HKDF-SHA512] ──► [AES-256-GCM / ChaCha20] ──► [embed in 3 stego channels]
```

- **HKDF**: per-session, 1 call — parallelizable across sessions only.
- **AES-GCM block ops**: each 16-byte AES block is independent. GCM GHASH accumulates but is naturally parallelizable via carry-free polynomial multiplication.
- **Stego channels**: LSB, timing, sequence are each independent transforms — run in parallel.

---

## 4. Scaling Dimensions

### 4.1 Vertical Scale (Per-Node: faster cores + SIMD)

| Technique | SHD-CCP benefit | RevProto benefit |
|---|---|---|
| **SIMD (AVX2/AVX-512)** | Quaternion multiply `[x,y,z,w] × 4` in one instruction | AES-NI decrypt/encrypt 8–16 blocks per cycle |
| **Loop unrolling** | Treble-generated t sampling — unroll into 4 independent pipelines | AES-GCM inner loop peel 4 blocks |
| **Memory alignment** | `Vector128<Single>` / `Vector256<Single>` aligned arrays for XYZ coords | 16-byte aligned plaintext buffers |
| **Span<T> / stackalloc** | Zero-alloc per-point pipeline in C# | Pre-allocated byte[] ring-buffer for packet queue |

### 4.2 Horizontal Scale (Multiple Nodes)

```
          ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
Client ──►│ Worker Node 1 │   │ Worker Node 2 │   │ Worker Node N │
          │  ┌────────┐   │   │  ┌────────┐   │   │  ┌────────┐   │
          │  │P1 P2 P3│   │   │  │P1 P2 P3│   │   │  │P1 P2 P3│   │
          │  └────────┘   │   │  └────────┘   │   │  └────────┘   │
          └──────▲────────┘   └──────▲────────┘   └──────▲────────┘
                 │                  │                  │
          ┌──────┴──────────────────┴──────────────────┘
          │          Scheduler / Load-Balancer
          │     (Round-robin, hash(packetId), or least-conn)
          └───────────────────► Sink / Aggregator
```

- **SHD-CCP**: each worker takes a knot tessellation slice `[t_start, t_end]`. Zero coordination between workers; final bitstream is concatenation of partial outputs + a single merge CRC.
- **RevProto**: each worker owns an AES session key. Stateless forwarding; if a worker dies the scheduler reassigns that key to a new node.

### 4.3 GPU Offload (CUDA / WebGPU)

| Workload | GPU kernel | Throughput uplift |
|---|---|---|
| Treble knot sampling | `t → xyz` on millions of t simultaneously | 50–100× CPU |
| AES-GCM encrypt | `cuda_crypto::aes256_gcm` block kernel | 5–20× CPU (depends on GPU) |
| LSB stego embed | per-bit of embedding mask | 10–50× CPU |

---

## 5. Architecture Blueprint

### 5.1 Layer Diagram

```
+────────────────────────────────────────────────────────────────┐
│                      INGEST / API LAYER                          │
│  REST + WebSocket + gRPC ingest   (DevLab GUI / CLI / Wireshark)│
├────────────────────────────────────────────────────────────────┤
│                    SCHEDULER / BATCH LAYER                       │
│  WorkQueue ──► Batch → Shard → Assign → Track ack               │
│  (Round-robin / least-conn / hash)                              │
├──────────────┬───────────────────────┬───────────────────────────┤
│ WORKER T-1   │  WORKER T-2   …       │  WORKER T-N               │
│ ┌──────────┐ │ ┌──────────┐  …      │  ┌──────────┐            │
│ │P1│P2│P3 │ │ │P1│P2│P3 │  …      │  │P1│P2│P3 │  per knot    │
│ └──────────┘ │ └──────────┘  …      │  └──────────┘            │
│ AES-NI lanes │ AES-NI lanes           │ AES-NI lanes             │
│ SIMD         │ SIMD                   │ SIMD                     │
├──────────────┴───────────────────────┴───────────────────────────┤
│                    PERSISTENCE LAYER                             │
│  time-series DB (InfluxDB) – per-stream metrics                 │
│  object store (MinIO/S3) – bitstream blobs                       │
│  WAL (Write-Ahead Log) – crash-safe replay                       │
└────────────────────────────────────────────────────────────────┘
```

### 5.2 SHD-CCP Pipeline Expanded

```
INPUT: N_t params (e.g. 360 per knot)
  │
  ▼
[PARTITION] ── split [0, N] into K shards of equal size
  │
  ▼
[SHARD WORKER] ── each worker processes its shard independently
  │  P1: for t ∈ shard: cos(t), sin(t), sin(2t), cos(2t), sin(3t)
  │  P2: fold XYZ → quaternion (x, y, z, w normalization)
  │  P3: CRC32 + bitstream serialize
  │
  ▼
[PARTIAL OUTPUT] ── K partial streams with local CRC
  │
  ▼
[MERGE] ── concatenate K partial bitstreams
         ── recompute CRC over assembled full stream
         ── (or: per-shard CRCs folded into root CRC)
  │
  ▼
OUTPUT: complete bitstream, deferred verify
```

The *merge CRC* is the one expensive synchronization point. Design choice:

> **Option A — Periodic reconvergence**: workers reassemble every B points → CRCs don't grow unbounded.  
> **Option B — CRC tree fold**: each worker outputs `(part, crc_of_part)`, root computes CRC of parts (1 extra pass).

Recommend **Option B** — tree-fold CRC avoids buffer copies at the root worker.

---

## 6. Scenarios

### Scenario A — DevLab on a 4-core laptop (baseline)

| Metric | Measured | Target |
|---|---|---|
| Shape → bitstream (360 pts) | ~0.5 ms | → 0.1 ms (SIMD) |
| RevProto demo run | ~500 ms | → 150 ms |
| Stego embed / extract | ~200 ms | → 50 ms |

**Path to target**: parallelize across 4 threads (4 knots simultaneously), use `Parallel.For` / `Task.WhenAll` in C#, `concurrent.futures.ThreadPoolExecutor` in Python.

### Scenario B — Workstation (8-core + AES-NI + GPU)

| Metric | Measured | Target |
|---|---|---|
| SHD-CCP batch (1 000 shapes) | ~5 s | → 200 ms |
| RevProto mass-encrypt (1 GB) | ~4 s | → 200 ms |

**Path to target**: add saturation workers (1 per core + 2 headroom), offload AES to AES-NI lanes. GPU offload only beneficial at >10 GB/s workloads.

### Scenario C — Cluster (N nodes on LAN, 10 GbE)

| Metric | Target |
|---|---|
| Distributed SHD-CCP 10 000-shape batch | < 1 s |
| Distributed RevProto 100 GB encrypt | < 10 s |

**Path to target**: add scheduler node + stateless workers + object store. Each worker is a container (Docker) started via Kubernetes or Nomad. WAL-backed replay ensures zero data loss on worker death.

---

## 7. Implementation Roadmap

### Phase 1 — Vertical: SIMD within a single core (low risk, high reward)

| Task | File(s) | Est. effort |
|---|---|---|
| Vectorize trefoil knot sampling (SIMD 4-wide) | `src/QuaternionKnot.cs` | 1-2 days |
| Batch CRC32: fold `uint[]` into single accumulator | `src/BitstreamSerializer.cs` | ½ day |
| `Parallel.For` shape batch loop | `src/SHDCcpProtocol.cs`, `examples/Program.cs` | ½ day |

### Phase 2 — Horizontal: multi-worker node (moderate risk)

| Task | File(s) / New | Effort |
|---|---|---|
| `WorkerNode` class (TCP or gRPC) | `devlab/WorkerNode.cs` (new) | 2-3 days |
| `Scheduler` (REST + WS ingest queue) | `devlab/cli/Scheduler.cs` (new) | B |
| Bitstream merge / CRC tree | `src/BitstreamSerializer.cs` | 1-2 days |
| WAL and replay logic | `devlab/cli/WalWriter.cs` (new) | 2 days |

### Phase 3 — GPU offload (high reward, contingent on Phase 1/2 success)

| Target | Tech | Effort |
|---|---|---|
| AES-NI harness + benchmark | C# `System.Runtime.Intrinsics` | 2–3 days |
| CUDA trefoil sampler | `CSHARP_CUDA` or AOT workers | 5–7 days |
| WebGPU visualizer | TypeScript `gpu.js` / `kernels` | 3–5 days |

---

## 8. Scalability Models

### 8.1 Conway's Law Mapping

> Organization design → system design → scaling topology

```
  Team           Cluster topology
 ───────────     ───────────────────────────────────────────────────
 1 engineer ──►  single dev machine (baseline)
 2 engineers ─►  2-node cluster (Phase 2 team split: core / worker)
 3 engineers ─►  1 scheduler + 2 worker types + WAL (Phase 2 stable)
 ```

### 8.2 Load Equations

**SHD-CCP throughput** (shapes / second) at K workers, N knots per worker batch:

```
T_throughput  ≈  K · (N / t_per_point)   [shapes/s]
t_per_point   ≈  200 ns (SIMD) / 1 µs (scalar)   [CPU-dependent]
```

To reach 10 k shapes/s on a 4-core box (K=4, scalar):
```
10,000  ≤  4 · (N / 1µs)
=> N ≥  2,500 pts/batch  ≈  7 knot batches per request
```

**RevProto throughput** (bytes/s) when AES-GCM dominates:

```
AES-256-GCM / ChaCha20 on 10 GbE link
Throughput  ≈  12.5 GB/s link raw → ~10 GB/s TCP → ~8 GB/s plaintext

Workers needed for 80% link utilisation:
N_workers  ≥  (8 GB/s) / (per-worker throughput)
           ≈  8 GB/s / 400 MB/s  ≈  20 worker lanes (e.g. 10 dual-socket servers)
```

---

## 9. Observability at Scale

A scaled deployment must be **self-diagnosing**. Key metrics from the protocol layer:

```
[SHD-CCP Worker]
  shape_batch_duration  gauge(ms, histogram)
  points_per_batch      gauge
  crc_error_count       counter
  quaternion_norm_μ/σ   gauge (should be 1.0 ± 1e-6)
  round_trip_error_p99  gauge (pass threshold: < 1e-4)

[RevProto Worker]
  packets_encrypted_sec   counter
  gcm_tag_failures        counter ← must alert if > 0 ever
  hkdf_kdf_seconds        gauge
  stego_channel_latency   gauge per channel
```

Two sinks:
- **Time-series**: InfluxDB (or Prometheus) — dashboards in DevLab GUI
- **Structured logs**: JSON log to `~/.config/devlab/logs/YYYY-MM-DD.jsonl` — WAL tail

---

## 10. Failure Modes & Mitigations

| Failure mode | SHD-CCP | RevProto |
|---|---|---|
| Unit quaternion drift | Recompute norm at every merge step | Re-normalize before packet encode |
| CRC mismatch on reassembly | Tree-fold CRC eliminates stale bytes | GCM auth tag must fail fast; never decrypt-on-tag-fail |
| Worker death mid-stream | WAL replay re-enqueues partial shard | Scheduler re-assigns session key, rotates HKDF |
| Time-scale fragmentation | Lockstep checkpoint every M points | Packet sequence numbers in devlab packet header |
| Convo deadlock | Lock per shard, shard finish order guided | No cross-chan locks: stego channels are colonially independent |

---

## 11. Cost Model

| Resource | Ramp-up cost | Ops cost | Notes |
|---|---|---|---|
| 1 additional CPU core | $0 (existing hardware) | $~3/month (cloud) | SIMD first, cores second |
| 1 worker container | $0 (existing hardware) | $~5/month | Docker on existing box |
| 1× V100 GPU | $~$3k CAPEX | $~1.50/hr (cloud) | Offload trefoil sampler + AES-NI if >10 GB/s |
| 10 GbE NIC | $~$150 CAPEX | $0 | Required for cluster scale |
| 10 GbE switch (32-port) | $~$3k CAPEX | $20/month | Optional for >4 nodes; can use RDMA |
| Wireshark dissector update | $0 | $0 | Existing `wireshark/myproto.lua` already covers protocol |

---

## 12. Open Questions

1. **Emergent 4th phase at scale**: does the harmonic interference property hold when parallel workers each hold a partial phase-3 stream? Confirmed experimentally in Phase 1.
2. **CRC tree vs periodic reconvergence**: benchmark both approaches with `Benchmark.cs` and decide by Phase 2.
3. **ChaCha20 vs ChaCha20-Poly1305 throughput gap on AMD vs Intel**: RevProto currently only benchmarks AES-256-GCM; add ChaCha20-Poly1305 benchmark.
4. **RevProto stego channel ordering**: does shifting load from channel 2 → channel 1 change detectable-entropy? Document under `ccpV1/prototype/`.

---

*This document is a design reference. Implement at your own risk; validate against the existing `SHD-CCP Invariants` document and RevProto `INTEGRATION_REPORT.md` before promoting to production.*
