# Weapon and animal attack reference

Text reference for `docs/weapons.bmp` (CST-1) and the animal statistics portion
of `docs/animalattack.bmp` (CST-2). Attack-chart results are transcribed separately
in [`merp-attack-tables.csv`](merp-attack-tables.csv).

## Weapons (CST-1)

An em dash means the source cell is blank. OB means Offensive Bonus.

| Weapon | Fumble | Primary critical | Secondary critical | Base range (ft) | Weight (lb) | Special modifications |
|---|---:|---|---|---:|---:|---|
| Broadsword | — | Slash | — | — | 4 | — |
| Dagger | — | Puncture (max C) | — | 15 | 1 | −15 OB |
| Handaxe | — | Slash | — | 15 | 5 | +5 OB against chain and plate |
| Scimitar | — | Slash | — | — | 4 | −5 OB against chain/plate; +5 OB otherwise |
| Short Sword | — | Slash | — | 3 | 3 | −10 OB against chain/plate; +10 OB otherwise |
| Club | — | Crush (max D) | — | 2 | 5 | −10 OB |
| Mace | — | Crush | — | 5 | 5 | — |
| Morning Star | 1 | Crush | Puncture (max A) | — | 5 | +10 OB; take a B critical if fumbled |
| Net | 1 | Grapple | — | 10 | 3 | +15 OB against chain/plate; −10 OB otherwise |
| War Hammer | — | Crush | — | 10 | 5 | +5 OB |
| Whip | 1 | Grapple (max C) | Slash (max A) | — | 3 | −10 OB; usable from second line |
| Javelin | — | Puncture | — | 30 | 4 | −10 OB; usable from second line |
| Spear | 1 | Puncture | Slash (max A) | 20 | 5 | −5 OB; usable from second line |
| Mounted Lance | 1 | Puncture | Unbalance | — | 10 | +15 OB; take a B critical if fumbled |
| Halberd | 1 | Slash | Puncture | — | 7 | −5 OB; usable from second line |
| Battle-axe | 1 | Slash | Crush | — | 7 | +5 OB against chain/plate; −5 OB otherwise |
| Flail | 1 | Crush | Puncture | — | 6 | +10 OB; take a C critical if fumbled |
| Quarterstaff | — | Crush | — | — | 4 | −10 OB |
| Two-Handed Sword | 1 | Slash | Crush | — | 8 | — |
| Bola | 1 | Grapple | Crush (max A) | 40 | 3 | −5 OB; take a B critical if fumbled |
| Composite Bow | — | Puncture | — | 75 | 3 | Load (1), or Reload (0) at −25 OB |
| Crossbow | 1 | Puncture | — | 90 | 8 | Load (2); +20 OB at up to 50 ft |
| Long Bow | 1 | Puncture | — | 100 | 3 | Load (1), or Reload (0) at −35 OB |
| Short Bow | — | Puncture | — | 60 | 2 | Load (1), or Reload (0) at −10 OB |
| Sling | 1 | Crush (max D) | — | 50 | 0.5 | Load (1); usable with a shield |

One-handed edged and concussion weapons may be used with a shield. One-handed
polearms may be used with a shield, or used two-handed for +10 OB. Two-handed
polearms and weapons require both hands. Missile weapons may not be used in melee.

On a 1d20 scale, a raw roll of 1 is always an automatic miss. Legacy d100 fumble ranges
are converted for 1d20: weapons with a legacy fumble chance of 5% or higher (1–5 through
1–8 in the source d100 tables) also suffer a weapon fumble on a raw d20 roll of 1. Weapons
with a legacy fumble chance below 5% (1–1 through 1–4) miss on a 1 but do not fumble.
For a non-missile weapon with a listed range, treat it as a thrown weapon.

Primary critical is the delivered critical type. A parenthesized letter is its
maximum severity; if absent, the maximum is E. A secondary critical is delivered
when the attack's critical is above B, at two severity steps lower, and is rolled
separately.

### Missile range bands

Base range is the weapon's listed Base Range. Short range is between one-half
base range and base range and has no OB modifier. Medium is from base to double
base at −25 OB; long is double to triple base at −50 OB; maximum is triple to
quadruple base at −75 OB.

| Base range | Short | Medium | Long | Maximum |
|---:|---:|---:|---:|---:|
| 2 | 1–2 | 3–4 | 5–6 | 7–8 |
| 3 | 1–3 | 4–6 | 7–9 | 10–12 |
| 5 | 1–5 | 6–10 | 11–15 | 16–20 |
| 10 | 1–10 | 11–20 | 21–30 | 31–40 |
| 15 | 1–15 | 16–30 | 31–45 | 46–60 |
| 20 | 1–20 | 21–40 | 41–60 | 61–80 |
| 30 | 1–30 | 31–60 | 61–90 | 91–120 |
| 40 | 1–40 | 41–80 | 81–120 | 121–160 |
| 50 | 1–50 | 51–100 | 101–150 | 151–200 |
| 60 | 1–60 | 61–120 | 121–180 | 181–240 |
| 75 | 1–75 | 76–150 | 151–225 | 226–300 |
| 90 | 1–90 | 91–180 | 181–270 | 271–360 |
| 100 | 1–100 | 101–200 | 201–300 | 301–400 |

## Animal attacks (CST-2)

| Attack type | Abbrev. | Primary attack chart | Primary critical | Secondary critical |
|---|---|---|---|---|
| Pincer / Beak | Pi | Tooth & Claw | Slash | Crush* |
| Bite | Bi | Tooth & Claw | Puncture | Slash (max C) |
| Claw / Talon | Cl | Tooth & Claw | Slash | Puncture (max B) |
| Horn / Tusk / Stinger | Ho or St | Tooth & Claw | Puncture | Crush (max C)* |
| Grapple / Grasp / Envelop / Swallow | Gr | Grappling & Unbalancing | Grapple | Unbalance (max C)* |
| Ram / Butt / Bash / Slam | Ra or Ba | Grappling & Unbalancing | Unbalance | Crush (max C)* |
| Tiny animals | Ti | Tooth & Claw | Slash (max T) | — |
| Stomp / Trample | Ts | Tooth & Claw | Crush | Crush* |
| Fall / Crush | Fa or Cr | Tooth & Claw | Crush | Crush* |
| Fist / Kick | Fi | Tooth & Claw | Unbalance (max A) | — |
| Wrestling / Tackles | Wr | Grappling & Unbalancing | Grapple (max A) | — |

`*` Only Large and Huge attacks receive the listed secondary critical.

If a character falls, add the distance fallen to the attack roll and subtract
the character's Agility bonus. Attack size is Small for 1–10 ft, Medium for
11–50 ft, Large for 51–100 ft, and Huge above 100 ft.

Fist/Kick and Wrestling/Tackles are humanoid hand-to-hand attacks. Their OB is
the attacker's Strength bonus plus Agility bonus.
