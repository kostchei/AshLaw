# ADR 0005: License under GPLv2, and reuse existing OSS rather than reimplement

- **Status:** Accepted
- **Date:** 2026-07-29

## Context

The specification (§18) names Pentagram and ScummVM's Ultima VIII engine as the
behavioural reference for this project, and the build plan (§2.5) instructs us to
**port** `SortItem::below` from `ultima8/world/sort_item.h`. ADR 0002 repeats that
framing.

Current ScummVM Ultima VIII source files are licensed **GPL-3.0-or-later**. Pentagram,
their predecessor and the original source of the sorter, is
**GPL-2.0-or-later**. Porting either implementation — translating C++ to C# — produces
a derivative work, but only Pentagram's version is compatible with this repository's
GPLv2-only license. Until this ADR, the repository had **no LICENSE file at all**, so
the sorter was written from geometric first principles instead of ported.

Two things had to be settled together: what this project is licensed as, and whether we
reimplement solved problems or take the existing solution.

## Decision

**1. This project is licensed under the GNU General Public License, version 2.** The
verbatim FSF text is in `LICENSE`.

**2. Prefer existing open-source implementations over reimplementation.** Where
Pentagram or ScummVM already solves a problem, port it with attribution rather than
deriving it again. Their code encodes years of special cases discovered against the real
game; rediscovering those from first principles is slower and produces a worse result.

Ported code must carry a comment naming the upstream file it came from, so provenance is
recoverable per-site and not just from this ADR. GPLv2-only code must use Pentagram or
another GPLv2-compatible source; current GPLv3-or-later ScummVM code is a behavioural
reference only.

## Consequences

### Accepted

- The project is copyleft. Anyone distributing it, modified or not, must offer
  corresponding source under GPLv2. Building this into a proprietary product is
  foreclosed. **This is effectively irreversible** — undoing it means getting agreement
  from every contributor and removing every ported line.
- `VolumeSorter.Below` is replaced by a real port from Pentagram's GPLv2-compatible
  `world/ItemSorter.cpp`. Its deterministic graph and cycle handling remain AshLaw
  code around the ported predicate.

### Dependency compatibility — checked, all clear

| Dependency | Licence | GPLv2-compatible |
|---|---|---|
| Godot.NET.Sdk 4.7.0 | MIT | Yes |
| MoonSharp (ADR 0003, not yet referenced) | BSD 3-Clause | Yes |
| Pentagram (source of GPLv2 ports) | GPL-2.0-or-later | Yes |
| Current ScummVM Ultima VIII source | GPL-3.0-or-later | No — behavioural reference only |
| Microsoft.NET.Test.Sdk | MIT | Yes |
| coverlet.collector | MIT | Yes |
| xunit 2.5.3 | Apache-2.0 | See note |

Apache-2.0 is not compatible with GPLv2 in a combined distributed work. xunit is a
test-only dependency: it is linked into `tests/*` assemblies, which are never
distributed as part of the game. This is fine as it stands, but **xunit must not become
a runtime dependency of anything under `src/` or `game/`.**

### Open — must be settled before any public distribution

1. **`vendor/ash-v1-rules` has no licence.** `PROVENANCE.md` states plainly: "No external
   license was present in the source directory… no third-party redistribution permission
   is asserted by this manifest." Distributing this repository under GPLv2 distributes
   that content too. It is the project owner's own material, so it is theirs to license —
   but it must be licensed explicitly, and `PROVENANCE.md` updated, before release.
2. **Game assets are not covered by this decision.** GPLv2 is a software licence. The
   original 256-colour art and audio commissioned at §5.4/§5.5 may warrant separate
   terms (CC-BY-SA, or proprietary with a GPL engine — both are common for games). Decide
   before commissioning, because it affects the artist's contract.
3. **Copyright headers are required on ported source files.** The first port,
   `VolumeSorter.cs`, carries Pentagram's 2003–2004 team copyright and its original
   GPL-2.0-or-later provenance beside AshLaw's GPLv2-only notice.

### Port record

The first port uses the official Pentagram SourceForge SVN repository:

- repository revision: `2560`;
- source: `pentagram/trunk/world/ItemSorter.cpp`;
- downloaded source SHA-256:
  `B6ECD76E3AA9CB1D70DDD1A1D501F1E1110E459095D33D0BCF62CC7D74831DB7`;
- license file: `pentagram/trunk/COPYING`;
- license SHA-256:
  `32B1062F7DA84967E7019D01AB805935CAA7AB7321A7CED0E30EBE75E5DF1670`.

The current ScummVM descendant was audited at master commit
`110ce8fa2151f94b951f82962d1ff491612d8ce4`. Its later implementation was not copied
because its file header is GPL-3.0-or-later.

### Not affected

Nothing here touches Ultima VIII's own content. No Origin data files are read, no
original assets are used, and the rules are an unrelated MERP/AD&D blend. The
relationship to Ultima VIII is one of genre and behaviour, which is what "Ultima
VIII-class" in the specification title means.
