# ADR 0005: License under GPLv2, and reuse existing OSS rather than reimplement

- **Status:** Accepted
- **Date:** 2026-07-29

## Context

The specification (§18) names Pentagram and ScummVM's Ultima VIII engine as the
behavioural reference for this project, and the build plan (§2.5) instructs us to
**port** `SortItem::below` from `ultima8/world/sort_item.h`. ADR 0002 repeats that
framing.

ScummVM is licensed **GPL-2.0-or-later**. Pentagram, its predecessor, was GPLv2. Porting
that code — translating C++ to C# — produces a derivative work. Until now the repository
had **no LICENSE file at all**, so there was nothing to reconcile that intent against,
and the sorter was written from geometric first principles instead of ported, precisely
because the question was open.

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
recoverable per-site and not just from this ADR.

## Consequences

### Accepted

- The project is copyleft. Anyone distributing it, modified or not, must offer
  corresponding source under GPLv2. Building this into a proprietary product is
  foreclosed. **This is effectively irreversible** — undoing it means getting agreement
  from every contributor and removing every ported line.
- `VolumeSorter.Below` should now be replaced by a real port. Its from-scratch geometric
  predicate was written under the previous uncertainty and is explicitly incomplete
  (build plan §6.4). Under this ADR it is the wrong approach, not merely unfinished.

### Dependency compatibility — checked, all clear

| Dependency | Licence | GPLv2-compatible |
|---|---|---|
| Godot.NET.Sdk 4.7.0 | MIT | Yes |
| MoonSharp (ADR 0003, not yet referenced) | BSD 3-Clause | Yes |
| ScummVM (source of ports) | GPL-2.0-or-later | Yes — this is the reason for GPLv2 |
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
3. **No copyright headers are in the source files yet.** GPL convention is a short notice
   per file. Add them when the first ported code lands, so the notice and the ported
   material arrive together.

### Not affected

Nothing here touches Ultima VIII's own content. No Origin data files are read, no
original assets are used, and the rules are an unrelated MERP/AD&D blend. The
relationship to Ultima VIII is one of genre and behaviour, which is what "Ultima
VIII-class" in the specification title means.
