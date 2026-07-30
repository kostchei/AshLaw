# Authoritative object store

`Ash.Sim.ObjectStore` is the sole owner of runtime object identity and mutable
object state. The playable slice no longer has separate player, chest, monster,
corpse, or inventory-item state classes.

## Identity

`ObjectId` is a 32-bit handle:

```text
31                 24 23                              0
+--------------------+--------------------------------+
| 8-bit generation   |         24-bit slot index      |
+--------------------+--------------------------------+
```

Generation zero is reserved for `ObjectId.None`. Destroying an object increments
its slot generation before the slot can be reused. Engine code dereferencing an
absent, destroyed, or stale handle gets `InvalidObjectIdException`;
`TryGet` is the explicit non-throwing boundary for scripts and optional runtime
references.

## One authoritative location

Every live object has exactly one `ObjectLocation`:

- `OnMap(map, Vec3i)`
- `InContainer(parent)`
- `Equipped(actor, slot)`
- `InTransfer(transaction)`

The tagged union deliberately does not expose a map position for a contained or
equipped object. Parent handles are validated, container cycles and duplicate
equipment slots are rejected, and container capacity is checked before a move
commits.

## Parallel components

Slots index parallel arrays for identity/presentation, location, physical
footprint and height, capabilities, quantity/condition, health, container
capacity, and open state. `WorldObject` is an immutable snapshot; mutation goes
through `ObjectStore`.

Capability flags define actors, monsters, containers, corpses, items, solid
objects, and other families without renderer-owned identities. Enumeration is
deterministic by slot.

The playable demo creates the Avatar, every chest, every monster, every piece of
loot, and every backpack item in this store. Monsters own their loot as contained
objects while alive. Death transforms the same monster handle into a corpse
container, preserving both identity and child objects.

## Invariants and current boundary

All mutation paths validate before committing. Debug builds run the full
invariant audit after structural mutations; `ValidateInvariants()` is also
available to headless tests. The audit covers:

- live count and handle generations;
- valid single locations and live parents;
- parent-graph acyclicity;
- container capability/capacity agreement;
- container capacity;
- unique equipment slots;
- positive quantity, valid health, and physical footprint.

This checkpoint intentionally does not implement the M2 spatial index, support
resolution, gravity, or save format. The next object-world layer is the map
container and commit-rebuilt uniform-grid spatial index.
