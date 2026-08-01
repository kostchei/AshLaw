# Monster encounter states

`Ash.Sim.CombatDirector` owns the vertical slice's monster encounter
state. It advances on the shared 200 ms combat beat and uses the world's seeded
`Dice`, so identical encounter inputs produce the same activity, reaction, and
timing rolls.

This is the first active-enemy implementation. It does not replace the broader
actor profiles, schedules, dialogue, or pathfinding planned for M6.

## Encounter lifecycle

A monster enters the encounter state machine when either:

- a living player comes within the six-tile awareness radius; or
- the player lands an adjacent attack before the monster has noticed them.

The initial state sequence is:

```text
unaware -> recognizing -> alerted -> engaged
               1d6 s         200 ms
```

During `recognizing`, the monster waits the rolled 1d6 seconds before resolving
the player. `Alerted` is a real decision beat: the chosen action cannot execute
until the following 200 ms combat tick. An engaged monster also pays one full
beat whenever it changes between pursuit and attack.

If an unprovoked monster loses the player beyond the awareness radius while it
is still recognizing, the encounter state is discarded. A later approach rolls
a fresh encounter. A monster attacked by the player remembers the provocation.

After engagement, hostile monsters are not bound to a room or encounter volume:

```text
engaged -> searching last known position -> returning home -> unaware
                    up to 3 seconds
```

These are creature states, not room states. Two monsters can notice the player
at different times, pursuit can cross an authored pacing beat, and fleeing into
another creature can leave both fights active.

## Activity roll

Activity is rolled once when an encounter is established.

| 2d6 | `MonsterActivity` | Description |
|---:|---|---|
| 2-4 | `HuntingOrStalking` | Looking for food or prey; aggressive stance |
| 5-6 | `EatingOrFeasting` | Distracted by a meal |
| 7-8 | `SearchingOrGathering` | Exploring, scavenging, or patrolling |
| 9-10 | `SocializingOrPlaying` | Interacting, performing rituals, or bickering |
| 11 | `GuardingOrResting` | Alert at a post or taking a minor break |
| 12 | `Sleeping` | Unconscious at encounter establishment |

Activity is currently descriptive state. Sleeping, distraction, stealth,
surprise, and activity-specific animations do not yet alter recognition or
combat resolution.

## Reaction roll

Unless the player attacks immediately, initial attitude is rolled as:

```text
2d6 + the player's Charisma modifier
```

The modifier comes from the same data-backed `AbilityBonusTable` used elsewhere
in the rules layer.

| Total | `MonsterReaction` | Current behavior |
|---:|---|---|
| 0-6 | `Hostile` | Pursues and attacks |
| 7-8 | `Suspicious` | Holds position |
| 9 | `Neutral` | Holds position |
| 10-11 | `Curious` | Holds position |
| 12+ | `Friendly` | Holds position |

A successful player attack forces `Hostile` and skips an unused reaction roll.
If the monster has already resolved its initial reaction, provocation schedules
the new hostile action one 200 ms beat later.

The non-hostile attitudes are represented independently now so later dialogue,
demands, trade, stealth, and faction systems can give them distinct actions.

## Hostile pursuit

An engaged hostile monster attacks when cardinally adjacent. Otherwise it
pursues the player with cardinal steps that reduce Manhattan distance.

- Movement is paced from the creature profile. Current level-one rates are
  15, 25, 30 or 50 feet across the six-second round (3, 5, 6 or 10 tiles).
- Every step is resolved by the shared `MovementSolver`.
- Successful steps commit through `ObjectTransferService`, which updates the
  authoritative `WorldMap` spatial index synchronously.
- Solid terrain, solid objects, occupied cells, elevation, support, gravity,
  and map bounds therefore apply to monsters under the same contracts as the
  Avatar.
- A blocked attempt consumes that movement interval and never overlaps or
  bypasses the blocker.
- An ordinary monster tracks within six tiles and will not travel more than
  eight tiles from the position where its encounter began.
- On losing contact it searches toward the last perceived player position for
  up to three seconds, then walks back to that home position and forgets the
  encounter.
- Cave rats, wolves and profiles carrying `PackHunter` alert same-type allies
  within five tiles and pursue out to sixteen tiles. `ScentTracker` and
  `EchoHunter` profiles track out to twelve tiles.

This slice does not yet route around a barrier. The monster tries the reducing
axis with the greatest remaining distance first, then the other reducing axis.
Full obstacle-routing pathfinding derived from shared spatial queries remains an
M6 task.

## State ownership and persistence

`ActivityOf`, `ReactionOf`, and `AwarenessOf` expose the current creature state
for presentation and tests. Save format 12 persists recognition, alerts,
actions, home position, last-known player position and last-perception tick, so
loading resumes a search or return rather than resetting nearby creatures.

Focused coverage lives in
`tests/Ash.Sim.Tests/CombatTimingTests.cs`: it checks the activity and
Charisma-adjusted reaction rolls, immediate provocation, 1d6 reaction timing,
the 200 ms decision beat, hostile pursuit, territorial search/return, pack
assistance, non-hostile idling, persistence, and collision/spatial-index
integrity.
