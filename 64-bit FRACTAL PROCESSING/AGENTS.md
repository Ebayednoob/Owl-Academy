# SHD-CCP Agent Documentation

## Overview

The **SHD-CCP Agent** is an autonomous agent responsible for managing the SHD-CCP (Shape-Harmonic Data - Closed Curve Protocol) protocol prototype system. This agent handles the full lifecycle of shape-to-bitstream conversions, quaternion knot topology operations, and protocol validation.

**Quick Navigation:**
- [Quick Start](#quick-start) — Get up and running in minutes
- [Core Responsibilities](#core-responsibilities) — What the agent does
- [Usage Examples](#usage-examples) — Common workflows
- [Detailed Reference](agent/shd-ccp-agent.md) — Full capabilities and constraints

---

## Quick Start

### Prerequisites

- .NET 8.0 SDK or later
- Kilo CLI (with SHD-CCP agent loaded)

### Building the Project

```bash
dotnet build SHDCCP.csproj
```

### Running the Agent

The agent can be invoked through Kilo when working within this project directory:

```bash
# The agent automatically activates when you work on SHD-CCP files
# Or explicitly specify the agent:
kilo --agent shd-ccp "convert shape to bitstream"
```

### Basic Workflow

1. **Shape Conversion**: The agent converts 3D geometric shapes (currently trefoil knots) into binary bitstreams
2. **Bitstream Serialization**: Serializes quaternion data and positional information with error detection
3. **Round-trip Validation**: Verifies reversibility through shape → bitstream → shape cycles

---

## Core Responsibilities

### Primary Functions

| Function | Description | Output |
|----------|-------------|--------|
| **Shape-to-Bitstream** | Converts 3D knot coordinates to binary representation | Binary packets with CRCs |
| **Bitstream-to-Shape** | Reconstructs 3D shapes from serialized data | 3D coordinate arrays |
| **Quaternion Operations** | Handles rotation, normalization, phase calculations | Normalized quaternions |
| **Protocol Validation** | Ensures data integrity and reversibility | Validation reports |
| **3D Visualization** | Renders trefoil knots with color-mapped data streams | Visual output files |

### Secondary Functions

- Unit test execution and result reporting
- Documentation generation and updates
- Binary serialization/deserialization with versioning
- Multi-stream packet orchestration

---

## Mathematical Foundation

### Trefoil Knot Parametric Equations

The core shape is parameterized as:

```
x = sin(t) + 2*sin(2t)
y = cos(t) - 2*cos(2t)
z = -sin(3t)
```

### Triple Stream Architecture

Each trefoil knot carries three interdependent data streams:

1. **Primary Stream** — Main data payload
2. **Inverse Stream** - Complementary data for verification
3. **Phase-shifted Stream** - Creates emergent 4th phase through harmonic interference

### Emergent 4th Phase

When all three streams are combined, they produce an emergent fourth phase:

```
P₄(t) = P₁(t) ⊕ P₂(t) ⊕ P₃(t + φ)
```

Where φ is the phase offset determined by the knot topology.

---

## Safety Protocols ⚠️

The agent enforces strict safety and validation protocols:

1. **Input Validation** — All shapes are validated before processing
2. **Error Detection** — Bitstreams include CRC checksums
3. **Quaternion Normalization** — All quaternions are normalized to unit length
4. **Round-trip Testing** — Every conversion is verified reversible (shape → bitstream → shape)
5. **Audit Logging** — All transformation steps are logged for traceability

### Invariant Checks

The agent verifies these invariants before accepting any operation:

- `shape.IsValid()` — Shape passes geometric validation
- `bitstream.CrcValid()` — Integrity check passes
- `quaternion.IsNormalized()` — |q| = 1.0 ± ε
- `roundTripError < threshold` — Reconstruction error within tolerance

---

## Usage Examples

### Example 1: Basic Shape Conversion

```csharp
// Generate a trefoil knot
var generator = new TrefoilKnotGenerator();
var shape = generator.Generate(resolution: 360);

// Convert to bitstream
var converter = new ShapeToBitstreamConverter();
var bitstream = converter.Convert(shape);

// Save to file
File.WriteAllBytes("trefoil.bin", bitstream.ToArray());
```

### Example 2: Multi-Stream Packet

```csharp
var packet = new ShdCcpPacket
{
    StreamId = Guid.NewGuid(),
    PrimaryStream = primaryData,
    InverseStream = Inverse(primaryData),
    PhaseStream = PhaseShift(primaryData, offset: Math.PI/3)
};

// Agent will automatically validate the packet
var result = agent.ProcessPacket(packet);
```

### Example 3: Round-trip Validation

```bash
# Build and run comprehensive test
dotnet test --filter "RoundTripTests"
```

Expected output:
```
RoundTripTests
  ✓ ShapeToBitstream_RoundTrip_PreservesGeometry
  ✓ BitstreamToShape_RecoversOriginalCoordinates
  ✓ MultiStream_Packet_Synchronization
  ✓ PhaseCalculation_EmergentFourthPhase_Accurate
```

---

## Architecture Overview

```
SHD-CCP Protocol Stack

┌─────────────────────────────────────────────────────────┐
│  Agent Control Layer                                    │
│  - Orchestrates all operations                          │
│  - Enforces safety protocols                            │
│  - Manages workflow state                               │
├─────────────────────────────────────────────────────────┤
│  Protocol Layer (SHDCcpProtocol.cs)                     │
│  - State machine                                         │
│  - Validation & verification                            │
│  - Error handling                                       │
├─────────────────────────────────────────────────────────┤
│  Conversion Layer                                        │
│  ├── ShapeToBitstreamConverter.cs                       │
│  ├── BitstreamToShapeConverter.cs                       │
│  └── QuaternionKnot.cs (parametric + ops)               │
├─────────────────────────────────────────────────────────┤
│  Serialization Layer                                     │
│  └── BitstreamSerializer.cs (CRC, versioning, packing)  │
├─────────────────────────────────────────────────────────┤
│  Output Layer                                            │
│  └── VisualizationRenderer.cs (3D rendering, export)    │
└─────────────────────────────────────────────────────────┘
```

---

## Frequently Asked Questions

<details>
<summary><strong>What shape types are supported?</strong></summary>
Currently only trefoil knots are fully supported. The architecture allows extension to other knot types (figure-eight, cinquefoil) but implementations are pending.
</details>

<details>
<summary><strong>How is the emergent 4th phase calculated?</strong></summary>
The 4th phase emerges from harmonic interference between the primary, inverse, and phase-shifted streams. Mathematically: P₄ = F⁻¹{F{P₁} · F{P₂} · F{P₃(φ)}}, where F denotes Fourier transform. See [QuaternionKnot.cs](../src/QuaternionKnot.cs) for implementation.
</details>

<details>
<summary><strong>What precision is used for quaternions?</strong></summary>
Single-precision floating point (float) with normalization tolerance of 1e-6. High-precision (double) mode is available via `UseHighPrecision = true` in the protocol configuration.
</details>

<details>
<summary><strong>How do I add a new knot type?</strong></summary>
Implement `IKnotGenerator` interface and register with `KnotRegistry`. See [Extension Guide](agent/shd-ccp-agent.md#extending-the-protocol) for details.
</details>

---

## Next Steps

- Read the **[Detailed Agent Reference](.kilo/agent/shd-ccp-agent.md)** for complete capability listing
- See **[Command Reference](.kilo/command/protocol-commands.md)`** for available CLI commands
- Review **[Implementation Guide](agent/shd-ccp-agent.md#implementation-details)** for extending the protocol

---

*Documentation maintained by the SHD-CCP Agent. Last updated: 2026-05-08*
