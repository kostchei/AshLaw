# Physical combat body tuning

The shared attack resolver returns the authored table's full damage. Simulation
does not clamp that result to a monster tier. Bodies instead have authored
concussion and wound pools in `CombatBalance`, and excess damage continues
through `ActorVitality` into wounds and the death clock.

The playable demo uses these pools:

| Monster | Concussion | Wounds | Intent |
| --- | ---: | ---: | --- |
| Cave Rat | 6 | 0 | A clean sword hit can finish it; a weak hit need not. |
| Goblin Scout | 10 | 0 | Usually survives a modest uncritical hit. |
| Goblin Guard | 16 | 0 | A durable melee opponent, but not critical-proof. |
| Many-Eyed Tyrant | 24 | 0 | Several ordinary hits or one severe critical. |

The generated level-one pool uses ten immutable `MonsterProfile` entries with
7–10 concussion, zero wounds, exact attack and defence modifiers, armour-table
category and movement rate. See [Level-one monsters](level-one-monsters.md).
Higher ranks retain the goblin-guard and many-eyed-tyrant scaling formulas.
This keeps resolver output inspectable and makes balance changes explicit
instead of hiding them behind damage caps.

Playable monsters currently have zero wounds deliberately: only the Avatar's
death clock is scheduled. A non-player wound pool would otherwise create an
inert dying monster whose saves never advance. Monsters therefore die when
their authored concussion pool is exhausted; adding NPC death clocks must be a
separate authored rules change, not an accidental side effect of this tuning.
