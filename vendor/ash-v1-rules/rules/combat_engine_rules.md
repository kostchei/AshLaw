# D20 Deterministic Combat Engine: Weapon vs. Armor System

This document outlines the core mathematical framework for a scalable, high-lethality d20 combat engine based on MERP (*Middle-earth Role Playing*). It replaces hit point bloat with deterministic lethality, armor-based damage reduction, and detailed single-roll critical trauma resolution.

For detailed trauma outcomes, see [critical_hits_system.md](critical_hits_system.md). For class attack modifier progressions (Level 1–30), see [class_progression_tables.md](class_progression_tables.md).

---

## 1. The Core Mechanic: The Net Roll

Every attack is resolved with a single d20 roll that factors in both the attacker's skill and the defender's evasion/parry capability.

**Net Roll = d20 + Attack Mod - Defense Mod**

If the Net Roll is equal to or greater than the target's **Hit Threshold** for the specific weapon or spell used, the attack lands.

---

## 1.1 Defense Modifier (DB) & Armor DEX Penalties

A defender's **Defense Mod (DB)** combines their agility, shield, parrying, and armor quality. Heavy armor imposes a base DEX penalty due to encumbrance, but a defender's **Strength Bonus (STR)** offsets this penalty (up to a maximum net penalty of 0).

### Armor DEX Penalty & Strength Compensation

$$\text{Effective DEX Penalty} = \min(0, \text{Base DEX Penalty} + \text{STR Bonus})$$
$$\text{Defense Mod (DB)} = \text{Base DB} + \text{DEX Bonus} + \text{Effective DEX Penalty} + \text{Shield Mod} + \text{Parry Mod}$$

| Armor Category | Base DEX Penalty | STR Bonus Needed for 0 Penalty | Effective DEX Bonus Calculation |
| :--- | :---: | :---: | :--- |
| **None** | 0 | +0 | Full DEX Bonus |
| **Leather** | 0 | +0 | Full DEX Bonus |
| **Chain** | -4 | +4 | $\text{DEX Bonus} + \min(0, -4 + \text{STR})$ |
| **Plate** | -8 | +8 | $\text{DEX Bonus} + \min(0, -8 + \text{STR})$ |

#### Resolution Examples:
* **Fighter in Plate** ($\text{DEX } +3$, $\text{STR } +4$): $\text{Effective Penalty} = \min(0, -8 + 4) = -4$. Net DEX contribution to DB = $+3 - 4 = -1$.
* **Fighter in Plate** ($\text{DEX } +3$, $\text{STR } +8$): $\text{Effective Penalty} = \min(0, -8 + 8) = 0$. Net DEX contribution to DB = $+3 + 0 = +3$ *(Full DEX unlocked!)*.
* **Fighter in Chain** ($\text{DEX } +2$, $\text{STR } +4$): $\text{Effective Penalty} = \min(0, -4 + 4) = 0$. Net DEX contribution to DB = $+2 + 0 = +2$ *(Full DEX unlocked!)*.

---

## 2. Calculating Hits (Concussion Damage)

Standard damage represents bruising, fatigue, and minor cuts. It scales linearly based on how much the Net Roll exceeds the armor's threshold.

**Hits = max(0, floor((Net Roll - Hit Threshold) * Multiplier))**

* **Hit Threshold:** The minimum Net Roll required to penetrate the armor.
* **Multiplier:** The rate at which damage scales. (Steep against unarmored, shallow against heavy armor).

---

## 3. Calculating Critical Severity & Trauma Sub-Result

Critical hits represent systemic physical trauma (broken bones, severed limbs, organ damage, instant death). They trigger on a step-function threshold rather than multiplying base damage:

**Crit Tier = floor((Net Roll - Base Crit Threshold) / Crit Interval)**

Map the resulting Crit Tier to the Severity Grade:
* **0** = Tier A (Minor Trauma)
* **1** = Tier B (Moderate Trauma)
* **2** = Tier C (Severe Trauma)
* **3** = Tier D (Lethal Trauma)
* **4+** = Tier E (Catastrophic/Instant Death)

*(Note: If the Crit Tier calculation results in a negative number, no critical hit is achieved).*

### Single-Roll Trauma Indexing
To determine the exact physical injury from [critical_hits_system.md](critical_hits_system.md) without rolling a 2nd die, take the **units digit** of the raw d20 roll (`raw d20 % 10`):
* `1 or 11` $\rightarrow$ Result 1 | `2 or 12` $\rightarrow$ Result 2 | ... | `10 or 20` $\rightarrow$ Result 10.

### 3.1 Activity Penalty & Stonetop Impairment Mapping
MERP's percentile activity penalties and permanent injuries translate seamlessly into modern d20 / D&D mechanics:
* **$-5\% \text{ to } -15\%$ Activity:** Flat **$-1$ or $-2$ penalty** to attack rolls, AC, and ability checks.
* **$-25\% \text{ to } -75\%$ Temporary Activity:** **Temporary Disadvantage** on attack rolls, physical checks, and DEX/STR saves for the specified duration.
### 3.2 Standard d20 Condition Mapping (Prone, Restrained, Incapacitated, Stunned, Unconscious)
MERP tactical conditions map directly to standard d20 / 5e conditions:
* **Knocked Down / Fall Down:** **`Prone`** (Melee attacks against have Advantage; costs half movement to stand).
* **Pinned / Constricted:** **`Restrained`** (Speed = 0, attacks against have Advantage, DEX saves at Disadvantage).
* **Stunned 1–2 Rounds / Bell Rung:** **`Incapacitated`** (Cannot take Actions or Reactions).
* **Heavy Stun (3+ Rounds) / Concussion:** **`Stunned`** (Incapacitated, speed = 0, auto-fails STR/DEX saves, attacks against have Advantage).
* **Coma / Unconscious / Passed Out:** **`Unconscious`** (Prone, drops items, auto-fails STR/DEX saves, melee hits within 5ft are auto-crits).
* **Bleed Damage & Dying:** Bleed damage ticks at turn start. Reaching 0 HP triggers standard **Death Saving Throws**.

---

## 4. MERP Core Attack Tables (AT-1 through AT-8)

The following tables define the parameters for calculating Hits and Criticals across all categories. Complete JSON configurations are maintained in [combat_system_data.json](../data/combat_system_data.json).


### 4.1 AT-1: 1-Handed Slashing (e.g., Longsword, Broadsword, Dagger, 1H Spear)
Swords are incredibly lethal against unarmored targets but struggle to transfer energy through rigid metal.

| Armor | Hit Threshold | Multiplier | Base Crit (A) | Crit Interval |
| :--- | :--- | :--- | :--- | :--- |
| **Plate** | 8 | 0.55 | 23 | 2 |
| **Chain** | 10 | 0.9 | 21 | 2 |
| **Leather** | 12 | 1.3 | 18 | 2 |
| **None** | 12 | 1.65 | 17 | 2 |

---

### 4.2 AT-2: 1-Handed Concussion (e.g., Mace, Warhammer, Club)
Blunt weapons have a flatter damage curve. They excel at transferring kinetic energy through plate armor to generate criticals via denting and crushing.

| Armor | Hit Threshold | Multiplier | Base Crit (A) | Crit Interval |
| :--- | :--- | :--- | :--- | :--- |
| **Plate** | 7 | 0.7 | 22 | 2 |
| **Chain** | 8 | 1.0 | 20 | 2 |
| **Leather** | 12 | 1.1 | 18 | 2 |
| **None** | 13 | 1.35 | 19 | 2 |

---

### 4.3 AT-3: 2-Handed Weapons & Polearms (e.g., Greataxe, Halberd, Pike, 2H Spear)
Includes two-handed swords, axes, halberds, pikes, and lances. Cannot be used with a shield. The heaviest damage curve in the system.

| Armor | Hit Threshold | Multiplier | Base Crit (A) | Crit Interval |
| :--- | :--- | :--- | :--- | :--- |
| **Plate** | 10 | 1.1 | 22 | 2 |
| **Chain** | 12 | 1.8 | 20 | 2 |
| **Leather** | 14 | 2.6 | 17 | 2 |
| **None** | 15 | 3.2 | 17 | 2 |

---

### 4.4 AT-4: Missile Weapons (e.g., Longbow, Shortbow, Heavy Crossbow)
Ranged attacks. The flattest spread in the system: plate absorbs most of an arrow's energy and caps out low, while soft targets take steadily escalating puncture damage.

| Armor | Hit Threshold | Multiplier | Base Crit (A) | Crit Interval |
| :--- | :--- | :--- | :--- | :--- |
| **Plate** | 14 | 1.0 | 22 | 2 |
| **Chain** | 15 | 1.6 | 20 | 2 |
| **Leather** | 15 | 1.7 | 18 | 2 |
| **None** | 18 | 2.2 | 19 | 2 |

---

### 4.5 AT-5: Tooth & Claw (e.g., Beast Bite, Claw, Horn, Animal Attacks)
Natural creature attacks like bites, claws, and horns.

| Armor | Hit Threshold | Multiplier | Base Crit (A) | Crit Interval |
| :--- | :--- | :--- | :--- | :--- |
| **Plate** | 10 | 1.2 | 21 | 2 |
| **Chain** | 11 | 1.4 | 20 | 2 |
| **Leather** | 11 | 1.8 | 18 | 2 |
| **None** | 9 | 1.9 | 16 | 2 |

---

### 4.6 AT-6: Grappling & Unbalancing (e.g., Tackle, Trip, Constrict, Pin, Unarmed Hold)
Unarmed holds, tackles, and creature constrictions. Heavy plate armor increases vulnerability to unbalancing, lowering the Base Crit threshold compared to unarmored targets.

| Armor | Hit Threshold | Multiplier | Base Crit (A) | Crit Interval |
| :--- | :--- | :--- | :--- | :--- |
| **Plate** | 11 | 0.9 | 14 | 3.5 |
| **Chain** | 13 | 1.3 | 17 | 3 |
| **Leather** | 13 | 1.6 | 20 | 2.5 |
| **None** | 12 | 1.8 | 21 | 3 |

---

### 4.7 AT-7: Directed Spell Bolts (e.g., Fire Bolt, Shock Bolt, Ice Bolt, Water Bolt)
Single-target elemental bolt rays. Different bolt types cap at specific MERP thresholds (Shock: Net Roll 18, Water: 22, Ice: 26, Fire & Lightning: 30).

| Armor | Hit Threshold | Multiplier | Base Crit (A) | Crit Interval |
| :--- | :--- | :--- | :--- | :--- |
| **Plate** | 7 | 0.8 | 13 | 4 |
| **Chain** | 8 | 1.0 | 15 | 3.5 |
| **Leather** | 10 | 1.3 | 15 | 3 |
| **None** | 12 | 2.0 | 13 | 2.2 |

---

### 4.8 AT-8: Area Spell Balls (e.g., Fireball, Coldball, Lightning Ball)
Area-of-effect elemental explosions that envelop targets in intense thermal or electrical energy. Criticals trigger very early even on low rolls (Base Crit A at Net Roll 5–9).

| Armor | Hit Threshold | Multiplier | Base Crit (A) | Crit Interval |
| :--- | :--- | :--- | :--- | :--- |
| **Plate** | 3 | 1.3 | 7 | 3.5 |
| **Chain** | 4 | 1.3 | 8 | 3.2 |
| **Leather** | 5 | 1.35 | 9 | 3 |
| **None** | 2 | 1.9 | 5 | 3.8 |

---

### 4.8 Environmental Fall & Impact Hazards
Kinetic falling damage and crushing environmental impacts.

| Armor | Hit Threshold | Multiplier | Base Crit (A) | Crit Interval |
| :--- | :--- | :--- | :--- | :--- |
| **Plate** | 12 | 1.5 | 17 | 3 |
| **Chain** | 10 | 1.5 | 16 | 3 |
| **Leather** | 8 | 1.5 | 15 | 3 |
| **None** | 6 | 1.5 | 14 | 3 |

---

## 5. Resolution Examples

### Scenario 1: Greataxe vs Plate
* Net Roll = 18 with 2-Handed Weapon vs Plate (`Hit Thresh = 10`, `Mult = 1.1`, `Base Crit A = 22`, `Interval = 2`).
* Hits = `floor((18 - 10) * 1.1)` = `floor(8.8)` = **8 Hits**.
* Crit Tier = `floor((18 - 22) / 2)` = **-2 (No Crit)**.

### Scenario 2: Giant Squid Tentacle vs Leather (Raw d20 = 17, Attack Mod = +5)
* Net Roll = `17 + 5 = 22` vs Leather Armor (`Hit Thresh = 8`, `Base Crit A = 17`, `Interval = 2`).
* Hits = `floor((22 - 8) * 1.8)` = **25 Hits**.
* Crit Tier = `floor((22 - 17) / 2)` = `floor(5 / 2)` = **2 (Tier C Severity)**.
* Sub-Result Index = `17 % 10` = **7**.
* Result from [critical_hits_system.md](critical_hits_system.md) (Grapple Tier C, Index 7): *"Dragged 10ft toward mouth. Stun 2 rds, prone."*

---

## 6. Spellcasting Constraints, Provocation & Mishaps

This section outlines tactical combat rules, class-based armor limitations, divine alignment requirements, and critical casting failure mechanics for all spellcasters.

### 6.1 Tactical Provocation (Attacks of Opportunity)
* **Melee Provocation:** Initiating a spell while within an opponent's active melee threat range provokes an **Attack of Opportunity (AoO)** from that opponent.
* **Resolution Timing:** The AoO resolves immediately prior to or during the spell completion. If the melee attack inflicts a Critical Hit or Heavy Stun, the spell casting attempt fails automatically.

### 6.2 Caster Class Armor & Shield Restrictions
Casting magical or primal forces requires physical fluidity or elemental harmony, imposing strict armor constraints based on spellcasting tradition:

| Class / Tradition | Allowed Armor & Shields | Casting Restrictions |
| :--- | :--- | :--- |
| **Wizard** | None | **Zero Armor.** Wizard spells cannot be cast while wearing any armor (Leather, Chain, or Plate) or carrying any shield. |
| **Druid** | Non-Metal Only | **No Metal Armor.** Druid spells cannot be cast while wearing metal armor (Chain, Plate) or wielding metal shields. Non-metal armors (Leather, Hide) and Wooden Shields are permitted. |
| **Cleric** | All Permitted | Can cast in any armor/shield type, subject to maintaining divine favor. |

### 6.3 Divine Faith Alignment: Cleric Virtues & Vices
Clerics derive their magical power directly from their deity's favor and alignment matrix:
* **Attribute Selection:** Every Cleric must select **5 specific Virtues or Vices** intrinsic to their deity.
* **Maintenance Thresholds:**
  * At least **3 Virtues/Vices** must be maintained at **15 or higher**.
  * The remaining **2 Virtues/Vices** must be maintained at **11 or higher**.
* **Loss of Divine Favor:** If a Cleric's scores fall below these minimum thresholds (due to moral failing, curse, or stat degradation), their divine connection is disrupted, suppressing their spellcasting ability until atonement or score restoration.

### 6.4 Natural 1 Spell Mishaps
* **Mishap Trigger:** If a spellcaster rolls a **natural 1** on a raw d20 spell roll (whether a directed spell bolt attack roll, area spell roll, or casting check), a **Spell Mishap** occurs.
* **Lockout Effect:** The magical energy unravels back upon the caster. The caster **cannot cast that specific spell until the next dawn**.
