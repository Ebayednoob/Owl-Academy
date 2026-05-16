# CLAUDE.md — Agent Context for the `ccp` Repository

This file is the canonical entry point for any agent (Claude Code, Kilo, Copilot, etc.) working in this repository. Read this first before making changes.

## What this repository is

This is a **mixed-purpose workspace** containing **three loosely related projects** plus shared infrastructure. Do not assume one global theme — the top-level `README.md` only covers one of the three projects.

| # | Project | Path | Language | Purpose |
|---|---------|------|----------|---------|
| 1 | **SHD-CCP Protocol** | `src/`, `examples/`, `SHDCCP.csproj` | C# / .NET 8.0 | Shape ↔ bitstream conversion via trefoil-knot quaternion algebra |
| 2 | **Kabbalah ↔ Connectome research** | `paper/` | Python 3.10+ | Graph-theoretic test of whether Kabbalistic tree diagrams match human brain topology |
| 3 | **CCP V1 / DevLab** | `ccpV1/` | Python + JS/TS + C# | Earlier "revolutionary protocol v2.0" prototype with crypto/stego + dev-tool integration (Wireshark, x64dbg, AngryIP) |

Reference docs (the top-level `README.md` is about project #2 only):
- Project #1: `PROJECT.md`, `AGENTS.md`, `.kilo/agent/shd-ccp-agent.md`, `.kilo/command/protocol-commands.md`
- Project #2: `paper/README.md`, `paper/paper.md`, `paper/AGENTS.md`
- Project #3: `ccpV1/DEVLAB_KEYFEATURES.md`, `ccpV1/prototype/README.md`, `ccpV1/AGENTS.md`
- Repo-wide: `docs/REPO_MAP.md`, `docs/SETUP.md`, `docs/GLOSSARY.md`

## Quick orientation by task

| If the user asks about... | Go to |
|---------------------------|-------|
| Trefoil knots, quaternions, bitstreams, SHD-CCP, `.csproj` | `src/`, `PROJECT.md`, `AGENTS.md` |
| Tree of Life, Daath, sephirot, connectome, Budapest RC | `paper/`, `README.md` |
| Wireshark dissectors, x64dbg, AngryIP, crypto+stego | `ccpV1/`, `ccpV1/DEVLAB_KEYFEATURES.md` |
| The "revolutionary protocol v2.0" demo / CLI / GUI | `ccpV1/prototype/`, `ccpV1/devlab/` |
| NVIDIA tile-level CUDA notes | `datenverarbeitung/konzept.txt` (isolated research note, unrelated to the rest) |
| 10 GbE C# techniques | `10GbE_CSharp_Whitepaper.md` (reference reading, not code) |

`docs/REPO_MAP.md` has a full directory-by-directory map.

## Build / run cheat sheet

See `docs/SETUP.md` for full details. TL;DR:

```bash
# SHD-CCP (C# protocol)
dotnet build SHDCCP.csproj
dotnet run --project SHDCCP.csproj

# Kabbalah-connectome research
cd paper && pip install -r requirements.txt && python3 run_analysis.py

# Revolutionary protocol v2.0 prototype
cd ccpV1/prototype && pip install -r requirements.txt && python run_prototype.py
```

## Repository conventions

- **`.gitignore` lists `.kilo/`, `.vscode/`, `.idea/`, `bin/`, `obj/`** — don't try to commit those.
- **`SHDCCP.csproj` uses explicit compile-item inclusion** (`<EnableDefaultCompileItems>false</EnableDefaultCompileItems>`) and explicitly globs `src/**` and `examples/**`. Adding a new C# file outside those directories will not compile unless you update the csproj.
- **Tests directory does not yet exist** despite docs mentioning `dotnet test`. There are no xUnit/NUnit projects in the tree. If the user asks to "run the tests", confirm what they mean before fabricating commands.
- **`ccpV1/src/`** has empty `components/`, `context/`, `hooks/`, `utils/` directories — placeholders, not real code.
- **`ccpV1/paperconcept/concept.md`** references HTML files (`phase_3d_v2-2.html`, etc.) via `../../../` — those HTML files are not in this repo. Treat broken links as known.
- **Many large image PDFs/PNGs at the root** (filenames starting with `ABS2GS...`, `AOI_...`) are research reference images, not project assets. Don't open them when exploring code.

## Existing agent metadata

- `kilo.json` already defines a Kilo "shd-ccp" primary agent with read-everywhere / edit-scoped permissions and a strict invariant list. If editing agent behavior, mirror those invariants.
- `AGENTS.md` (root) is the SHD-CCP agent's overview — keep it in sync if you change protocol semantics.
- `.kilo/agent/shd-ccp-agent.md` is the full reference; `.kilo/command/protocol-commands.md` is the API surface.

## Safety invariants (SHD-CCP only)

When working in `src/` or `examples/`, never bypass:

1. `shape.IsValid()` — geometric validation must pass
2. `|q| = 1.0 ± 1e-6` — quaternions stay normalized
3. CRC32 checksums on bitstreams — never disable
4. Round-trip error < 1e-4 (configurable, but don't relax silently)
5. Three streams (primary / inverse / phase-shifted) stay length-synchronized

If you change the wire format, bump the version byte in the header and document the migration path.

## What NOT to do

- Don't rewrite `README.md` to "match the rest of the repo" — it intentionally documents project #2 only.
- Don't merge the three projects' build systems. They're independent on purpose.
- Don't add docs to `.kilo/` expecting them to be tracked — that directory is gitignored.
- Don't run `dotnet test` and report success without checking that a test project exists.
- Don't open the giant PNGs/PDF at the root unless the user asks about them specifically.

## Where to put new docs

- Agent-facing instructions → this file (`CLAUDE.md`) or `AGENTS.md` files in subprojects
- Human-facing project docs → `docs/` (cross-cutting), `PROJECT.md` (SHD-CCP), or per-subproject READMEs
- Per-subproject agent context → `<subproject>/AGENTS.md`
