# ADR 0002: Sort visible objects as a volume-relation graph

- **Status:** Accepted
- **Date:** 2026-07-29

## Context

Ultima VIII-style objects occupy horizontal footprints and vertical intervals. Their
pairwise front/behind relationship is not transitive, so using it as a comparator for
`List.Sort` can produce cycles, unstable output, and visible flicker.

## Decision

The renderer will:

1. derive pairwise ordering from each visible object's footprint and vertical interval,
   using ScummVM Ultima VIII `SortItem::below` as the behavioral reference;
2. represent those relations as a directed graph;
3. produce draw order with a topological sort;
4. detect cycles and break them deterministically using
   `(x_min + y_min, z_min, ObjectId)`;
5. emit cycle diagnostics for authoring tools and support explicit per-shape sort bias.

Candidate comparisons are reduced with screen-space buckets. M1 must benchmark 2,000
visible objects at 60 Hz before this design is considered proven.

## Consequences

- Fixed input produces stable draw order, including ambiguous arrangements.
- Sort cycles become visible content/tooling problems instead of silent frame flicker.
- The sorter is more complex than a comparison sort and needs graph-focused tests.
- The initial worst-case cost is quadratic; bucketing and measurement are mandatory.

