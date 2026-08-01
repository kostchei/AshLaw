# Level-one monster catalogue

The level-one pool contains five adaptations and five stable results from
`MonsterGenerator.GenerateLevelOne(20260801, 5)`. The generator implements the
four independent d20 columns on Shadowdark's **Monster Generator** table:
Combat, Quality, Strength, and Weakness.

For this roster, Combat is rerolled until it equals party level (PL), because
these ten candidates are specifically the level-one pool. At PL 1 that gives
LV 1, ATK +1, and AC 11. Quality, Strength, and Weakness remain unrestricted
independent d20 rolls. Base damage is 1d8, but this project deliberately caps
all level-one monsters at one attack. A rolled `+1 attack` or `+2 attacks`
strength is retained as advancement data and does not bypass that cap.

Shadowdark HP remains on each profile. The Ash combat adapter maps one HP to
two concussion so these small values survive the resolver's damage scale. AC
maps to intrinsic DEF as `AC - 10`. Attack count and damage dice are preserved
as content even though the current real-time attack scheduler still resolves
one attack profile per action.

## Adapted monsters

| Monster | AC | HP / concussion | Attack | ATK | Move | Special | Weakness |
| --- | ---: | ---: | --- | ---: | --- | --- | --- |
| Bandit | 13 | 4 / 8 | 1 club, 1d4 | +1 | 30 ft ground | Skirmisher | Greed |
| Beastman | 12 | 5 / 10 | 1 spear, 1d6+1 | +2 | 30 ft ground | Scent tracker | Fire |
| Giant Centipede | 11 | 4 / 8 | 1 bite, 1d4 + venom | +1 | 30 ft climb | Venomous | Cold |
| Darkmantle | 13 | 4 / 8 | 1 bite, 1d4 | +3 | 30 ft fly | Darkness aura | Light |
| Giant Rat | 11 | 5 / 10 | 1 bite, 1d4 + disease | +1 | 30 ft ground | Pack hunter | Fire |

## Seed-generated monsters

| Monster | Combat / Quality / Strength / Weakness rolls | AC | HP / concussion | Attacks | Armour type | Move |
| --- | --- | ---: | ---: | --- | --- | --- |
| Arachnid Sage | 8 / 5 Arachnid / 9 Highly intelligent / 10 Silver | 11 | 3 / 6 | 1 bite, 1d8 | None | 30 ft climb |
| Amphibious Glare | 13 / 3 Amphibious / 16 Blinding aura / 4 Salt | 11 | 8 / 16 | 1 bite, 1d8 | None | 30 ft swim |
| Elephantine Reaver | 13 / 10 Elephantine / 14 1d12 damage / 14 Garlic | 11 | 5 / 10 | 1 crush, 1d12 | None | 30 ft ground |
| Piscine Mind | 9 / 19 Piscine / 11 Psychic blast / 11 Fire | 11 | 4 / 8 | 1 ranged bite, 1d8 | None | 30 ft swim |
| Beastlike Veil | 8 / 1 Beastlike / 17 Turns invisible / 6 Mirrors | 11 | 4 / 8 | 1 bite, 1d8 | None | 30 ft ground |

Every generated profile stores its four die results, quality, strength,
weakness, AC, HP, armour type, attack count, damage dice, attack bonus,
movement mode, ability scores, voice, and carried loot. The seed and result
index are part of stable authored content, so type IDs safely survive save/load.

## Boss mutation

The creature guarding the reward is the subzone boss. At level one it receives
one d12 roll from **Mutation 1**: Shapechanger, Fins and gills, Insulating fur,
Ironlike scales, Extra limbs, Tentacles, Boneless, Gigantic, Flings spikes, Two
heads, Burrows, or Wings.

The mutation is encoded in the monster type ID. Loading a save reconstructs
the same profile, visible name, description, movement mode, and attack profile.
Wings, Burrows, and Fins and gills provide their matching movement; Tentacles
use the restraining attack category; Flings spikes provides a ranged attack;
and Gigantic uses a large attack and faster movement. A mutated boss remains
LV 1 in combat but has treasure level 3, following the rule that mutations
count as two extra levels only when rolling treasure.

The five-beat subzone places one threat in the encounter space and this mutated
boss beside the reward: two threatened spaces out of five (40%). They are
independent creatures, not room objectives. They may overlap, be evaded,
bribed, or lured; killing either awards zero XP. Emptying the vault's legendary
hoard awards the 10 XP required for level two.
