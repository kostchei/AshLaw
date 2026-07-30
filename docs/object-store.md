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

## Transactional transfer

`ObjectTransferService` is the single movement boundary for the world,
containers, actor backpacks, and equipment. Each request carries an object
handle, its expected source, and its destination. A transaction:

1. snapshots the current object graph;
2. verifies every handle and expected source;
3. projects every requested destination together;
4. validates parents, cycles, final container counts, equipment capability,
   accepted slots, and final slot uniqueness;
5. commits all locations together only after the complete projection passes.

This final-state validation permits atomic swaps between full containers and
between occupied equipment slots without inventing a temporary invalid state.
Stale-source, capacity, cycle, and equipment failures return a typed
`ObjectTransferFailure` and mutate nothing. The legacy single-object `Move`
entrypoint routes through the same transaction service.

Items declare accepted slots with `EquipmentSlotMask`. Equipment slots reference
the ordinary item handle; equipping therefore preserves identity, quality,
quantity, condition, and presentation data.

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
- equipped-item slot restrictions;
- positive quantity, valid health, and physical footprint.

This checkpoint intentionally does not implement the M2 spatial index, support
resolution, gravity, or save format. The next object-world layer is the map
container and commit-rebuilt uniform-grid spatial index.
