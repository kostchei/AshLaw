# ADR 0001: Use integer world coordinates

- **Status:** Accepted
- **Date:** 2026-07-29

## Context

The simulation must be deterministic, byte-stable across save/load, and free from
position drift. Variable-rate floating-point movement makes collision, support, replay,
and persistence sensitive to rounding and platform details.

## Decision

`Ash.Sim` uses signed 32-bit integer world coordinates:

- one tile is 256 world units;
- one vertical level is 8 world units;
- velocity is expressed in world units per simulation tick;
- simulation and collision arithmetic do not use floating point.

Powers of two are retained if either scale is tuned. Floating-point conversion is
allowed only at the rendering/projection boundary.

## Consequences

- Simulation state, collision results, replay hashes, and serialized positions are
  exact and reproducible.
- Sub-unit motion requires an integer remainder or fixed-point accumulator.
- Overflow must be checked when multiplying coordinates or calculating bounds.
- Rendering code must explicitly convert simulation coordinates to screen-space
  floating-point values.

