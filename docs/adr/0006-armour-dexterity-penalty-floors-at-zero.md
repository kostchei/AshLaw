# ADR 0006: An armour dexterity penalty reduces the bonus, it does not invert it

- **Status:** Accepted
- **Date:** 2026-07-31

## Context

The vendored Ash V1 combat rules offset an armour's dexterity penalty with the
wearer's strength bonus:

```text
Effective DEX Penalty = min(0, Base DEX Penalty + STR Bonus)
Defense Mod (DB)      = Base DB + DEX Bonus + Effective DEX Penalty
                      + Shield Mod + Parry Mod
```

Read literally, the subtraction can carry past zero. The rules' own worked
example does exactly that:

> **Fighter in Plate** (DEX +3, STR +4): Effective Penalty = min(0, −8 + 4) = −4.
> Net DEX contribution to DB = +3 − 4 = **−1**.

Two further facts make this load-bearing. Ability bonuses are
`floor((score − 10) / 2)` over scores of 3 to 20, so the mortal maximum is +5;
and plate's −8 therefore cannot be fully offset by any unmagical character.
Under a literal reading, every mortal in plate defends *worse* than the same
character standing still with no agility at all, and the strongest characters
are penalised least for a suit that is equally heavy for everyone.

## Decision

The armour penalty consumes the dexterity bonus and stops:

```text
Effective DEX Penalty = min(0, Base DEX Penalty + STR Bonus)
DEX Contribution      = max(0, DEX Bonus + Effective DEX Penalty)
Defense Mod (DB)      = Base DB + DEX Contribution + Shield Mod + Parry Mod
```

Armour can take away what agility was giving you. It cannot make you clumsier
than a character with no agility to lose.

Consequences of the curve stand: chain (−4) is fully offset at Strength 18, and
plate (−8) never is, so a mortal in plate gets no dexterity contribution at all
unless magic lifts strength past the ceiling. Plate's defence has to come from
its own base defence, which is where a heavy suit's value belongs.

## Divergence from the vendored rules

This contradicts the worked example quoted above. The vendored package is not
edited: it stays as sourced, and `PROVENANCE.md` remains the record of what came
from where. This ADR is the record of where U8_Ash deliberately differs, and
`DefenseDerivation` carries the same note at the point of use.

If a later source chart shows the negative contribution was intended, the change
is one `Math.Max` and this ADR is superseded rather than quietly reversed.

## Consequences

- `DefenseDerivation` is the only place the formula lives; UI, AI and scripts
  read derived values rather than recomputing them.
- A test asserts the divergent case (DEX +3, STR +4, plate) resolves to 0 and
  names this ADR, so the difference cannot be "fixed" back by accident.
- The armour dexterity penalties themselves (chain −4, plate −8) exist only in
  the rules prose, not in `combat_system_data.json`. They are transcribed in
  `DefenseDerivation` beside the table they came from, and should move into
  runtime data when armour gains its own authored definitions.
