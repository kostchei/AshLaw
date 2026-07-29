# Class Attack Modifier Progression Specification

This document defines four level-based Attack Modifier ($\text{OB}$) progression
curves. Fighter, Cleric, Rogue, and Wizard are the curve archetypes; the eight
additional playable classes reuse those curves as described below. The system
blends AD&D 1st/2nd Edition class archetypes with Rolemaster 10-level diminishing
brackets and hard caps.

For combat resolution rules and damage calculation, see [combat_engine_rules.md](combat_engine_rules.md) and [critical_hits_system.md](critical_hits_system.md). Complete data is configured in [combat_system_data.json](../data/combat_system_data.json).

---

## 1. System Design Principles

1. **Rolemaster 10-Level Brackets:**
   - **Bracket 1 (Levels 1–10):** Prime martial growth period.
   - **Bracket 2 (Levels 11–20):** Secondary growth period (rate drops by 1 step).
   - **Bracket 3 (Levels 21+):** Extended growth period (rate drops by 1 step or maintains floor).
2. **Standard Fractional Progression Steps:**
   - Progression rates move along the standard sequence $\{+1, +2/3, +1/2, +1/3\}$.
3. **Minimum Active Rate Rule:**
   - No active progression rate drops below **$+1/3 / \text{level}$** (+1 every 3 levels) until the hard cap is reached.
4. **Hard Caps (AD&D Reversed THAC0 Equivalence):**
   - **Fighter:** Cap $+20$
   - **Cleric:** Cap $+15$
   - **Rogue:** Cap $+10$
   - **Wizard:** Cap $+6$

---

## 2. Progression Rate Brackets Summary

| Class | Bracket 1 (Lv 1–10) | Bracket 2 (Lv 11–20) | Bracket 3 (Lv 21+) | Hard Cap | Level Cap Reached |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Fighter** | $+1 / \text{lv}$ | $+2/3 / \text{lv}$ | $+1/2 / \text{lv}$ | **+20** | Level 26 |
| **Cleric** | $+2/3 / \text{lv}$ | $+1/2 / \text{lv}$ | $+1/3 / \text{lv}$ | **+15** | Level 29 |
| **Rogue** | $+1/2 / \text{lv}$ | $+1/3 / \text{lv}$ | $+1/3 / \text{lv}$ | **+10** | Level 26 |
| **Wizard** | $+1/3 / \text{lv}$ | $+1/3 / \text{lv}$ | *Capped* | **+6** | Level 19 |

### 2.1 Additional class curve mapping

| Added class | Reused curve | Reason |
| :--- | :---: | :--- |
| **Bard** | Cleric | Martial/druidic hybrid |
| **Barbarian** | Fighter | Prime martial |
| **Sorcerer** | Wizard | Full defiler caster |
| **Paladin** | Fighter | Prime martial with late divine magic |
| **Celestial Warlock** | Wizard | Full pact caster |
| **Monk** | Cleric | Secondary martial progression |
| **Ranger** | Fighter | Prime martial |
| **Assassin** | Rogue | Thief subclass |

The published CSV repeats the selected archetype's values in a named column for
each added class. This is intentional: callers resolve a concrete character
class without needing a second alias lookup.

---

## 3. Full Level 1–30 Progressive Curve Table

| Level | Fighter (Cap +20) | Cleric (Cap +15) | Rogue (Cap +10) | Wizard (Cap +6) | Phase / Milestone |
| :---: | :---: | :---: | :---: | :---: | :--- |
| **1** | +1 | +1 | +0 | +0 | **Bracket 1 (Levels 1–10)** |
| **2** | +2 | +1 | +1 | +0 | Fighter: $+1/\text{lv}$ |
| **3** | +3 | +2 | +1 | +1 | Cleric: $+2/3/\text{lv}$ |
| **4** | +4 | +3 | +2 | +1 | Rogue: $+1/2/\text{lv}$ |
| **5** | +5 | +3 | +2 | +1 | Wizard: $+1/3/\text{lv}$ |
| **6** | +6 | +4 | +3 | +2 | |
| **7** | +7 | +5 | +3 | +2 | |
| **8** | +8 | +5 | +4 | +2 | |
| **9** | +9 | +6 | +4 | +3 | |
| **10** | +10 | +7 | +5 | +3 | End of Bracket 1 |
| **11** | +11 | +7 | +5 | +3 | **Bracket 2 (Levels 11–20)** |
| **12** | +11 | +8 | +5 | +3 | Fighter drops to $+2/3/\text{lv}$ |
| **13** | +12 | +8 | +6 | +4 | Cleric drops to $+1/2/\text{lv}$ |
| **14** | +13 | +9 | +6 | +4 | Rogue & Wizard use $+1/3/\text{lv}$ |
| **15** | +13 | +9 | +6 | +4 | |
| **16** | +14 | +10 | +7 | +5 | |
| **17** | +15 | +10 | +7 | +5 | |
| **18** | +15 | +11 | +7 | +5 | |
| **19** | +16 | +11 | +8 | +6 | **Wizard Cap (+6) Reached** |
| **20** | +17 | +12 | +8 | +6 | End of Bracket 2 |
| **21** | +17 | +12 | +8 | +6 | **Bracket 3 (Levels 21+)** |
| **22** | +18 | +12 | +8 | +6 | Fighter drops to $+1/2/\text{lv}$ |
| **23** | +18 | +13 | +9 | +6 | Cleric & Rogue use $+1/3/\text{lv}$ |
| **24** | +19 | +13 | +9 | +6 | |
| **25** | +19 | +13 | +9 | +6 | |
| **26** | +20 | +14 | +10 | +6 | **Fighter (+20) & Rogue (+10) Caps Reached** |
| **27** | +20 | +14 | +10 | +6 | |
| **28** | +20 | +14 | +10 | +6 | |
| **29** | +20 | +15 | +10 | +6 | **Cleric Cap (+15) Reached** |
| **30** | +20 | +15 | +10 | +6 | Max Cap State |

---

## 4. Key System Highlights

* **Fighter:** Progression steps $+1 \rightarrow +2/3 \rightarrow +1/2$. Reaches hard cap of **+20** at Level 26.
* **Cleric:** Progression steps $+2/3 \rightarrow +1/2 \rightarrow +1/3$. Reaches hard cap of **+15** at Level 29.
* **Rogue:** Progression steps $+1/2 \rightarrow +1/3 \rightarrow +1/3$. Reaches hard cap of **+10** at Level 26.
* **Wizard:** Progression steps $+1/3 \rightarrow +1/3 \rightarrow \text{Capped}$. Reaches hard cap of **+6** at Level 19.

---

## 5. Class Spellcasting & Special Constraints

For full combat and tactical spellcasting rules, see [combat_engine_rules.md](combat_engine_rules.md#6-spellcasting-constraints-provocation--mishaps).

* **Wizard:**
  * **Armor Restriction:** Cannot cast spells while wearing any armor (Leather, Chain, Plate) or shield.
  * **Spell Mishap:** Natural 1 on spell roll locks out that specific spell until next dawn.
  * **Tactical:** Casting in melee provokes an Attack of Opportunity (AoO).
* **Druid:**
  * **Armor Restriction:** Cannot cast spells while wearing metal armor or wielding metal shields. Non-metal armor (leather/hide) and wooden shields are allowed.
  * **Spell Mishap:** Natural 1 on spell roll locks out that specific spell until next dawn.
  * **Tactical:** Casting in melee provokes an Attack of Opportunity (AoO).
* **Cleric:**
  * **Armor Restriction:** Unrestricted armor usage.
  * **Divine Virtue/Vice Alignment:** Must select 5 virtues/vices for their deity and maintain **3 at $\ge 15$** and **2 at $\ge 11$** to retain spellcasting powers.
  * **Spell Mishap:** Natural 1 on spell roll locks out that specific spell until next dawn.
  * **Tactical:** Casting in melee provokes an Attack of Opportunity (AoO).

---

## 6. Class Talent & Progression Tables (Levels 1–20)

### 6.1 Exact Target Stat Gains & 2d6 Probability Tuning

To achieve your exact target number of stat gains across each class's open level budget, the 2d6 stat bands are calibrated as follows:

* **Thief:** Target **5 Stat Gains** across **12 Open Slots** $\rightarrow$ Stat Band **7–9** ($15/36 = 41.7\%$) $\rightarrow \mathbf{12 \times 41.7\% = 5.00}$ (EXACTLY 5!)
* **Fighter:** Target **6 Stat Gains** across **15 Open Slots** $\rightarrow$ Stat Band **7–9** ($15/36 = 41.7\%$) $\rightarrow \mathbf{15 \times 41.7\% = 6.25}$ ($\approx 6$)
* **Priest:** Target **4 Stat Gains** across **10 Open Slots** $\rightarrow$ Stat Band **7–9** ($15/36 = 41.7\%$) $\rightarrow \mathbf{10 \times 41.7\% = 4.17}$ ($\approx 4$)
* **Wizard:** Target **4 Stat Gains** across **8 Open Slots** $\rightarrow$ Stat Band **6–9** ($18/36 = 50.0\%$) $\rightarrow \mathbf{8 \times 50.0\% = 4.00}$ (EXACTLY 4!)

| Class | Open Slots | Target Stat Gains | Stat Roll Band | Stat Probability | Expected Stat Gains | Expected Stat Pts | Expected Class Talents |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **Thief** | 12 Slots | **5 Gains** | **7–9** (15/36) | 41.7% | **5.00 Gains** | **+10.0 Pts** | **7.0 Talents** |
| **Fighter** | 15 Slots | **6 Gains** | **7–9** (15/36) | 41.7% | **6.25 Gains** | **+12.5 Pts** | **8.75 Talents** |
| **Priest** | 10 Slots | **4 Gains** | **7–9** (15/36) | 41.7% | **4.17 Gains** | **+8.3 Pts** | **5.83 Talents** |
| **Wizard** | 8 Slots | **4 Gains** | **6–9** (18/36) | 50.0% | **4.00 Gains** | **+8.0 Pts** | **4.0 Talents** |

---

### 6.2 Calibrated 2d6 Class Talent Tables

#### Thief Talent Table (12 Open Slots $\rightarrow$ Target 5 Stat Gains)
* **Stat Band:** Rolls **7–9** (41.7% chance). Yields **EXACTLY 5.00 stat gains** ($+10$ stat points) and 7 talents over 12 rolls.

| 2d6 Roll | Probability | Feature / Ability |
| :---: | :---: | :--- |
| **2** | 2.8% | **Shadow Wildcard:** $+2$ to ANY attribute OR choose any option on this table. |
| **3–6** | 38.9% | **Agile Lethality & Thievery:** $+1$ to Attack/Damage when Backstabbing OR gain Advantage on a chosen Thief Skill. |
| **7–9** | **41.7%** | **Furtive Attribute Boost:** $+2$ to **Dexterity**, **Intelligence**, or **Charisma** (or $+1$ to two of these stats). |
| **10–11** | 13.9% | **Evasion & Reflexes:** $+1$ AC while unarmored/wearing light armor OR $+1$ to Initiative rolls & Reflex saves. |
| **12** | 2.8% | **Shadow Wildcard:** $+2$ to ANY attribute OR choose any option on this table. |

#### Fighter Talent Table (15 Open Slots $\rightarrow$ Target 6 Stat Gains)
* **Stat Band:** Rolls **7–9** (41.7% chance). Yields **6.25 stat gains** ($\approx 6$ boosts, $+12$ stat points) and 8.75 talents over 15 rolls.

| 2d6 Roll | Probability | Feature / Ability |
| :---: | :---: | :--- |
| **2** | 2.8% | **Mastery Wildcard:** $+2$ to ANY attribute OR choose any option on this table. |
| **3–6** | 38.9% | **Weapon Specialization & Combat Style:** $+1$ to Attack & Damage rolls with one weapon type, OR $+1$ AC while wearing armor. |
| **7–9** | **41.7%** | **Martial Attribute Boost:** $+2$ to **Strength**, **Dexterity**, or **Constitution** (or $+1$ to two of these stats). |
| **10–11** | 13.9% | **Combat Vitality & Lethality:** $+1$ HP per level (retroactive) OR $+1$ to Melee/Ranged Critical Hit severity. |
| **12** | 2.8% | **Mastery Wildcard:** $+2$ to ANY attribute OR choose any option on this table. |

#### Priest Talent Table (10 Open Slots $\rightarrow$ Target 4 Stat Gains)
* **Stat Band:** Rolls **7–9** (41.7% chance). Yields **4.17 stat gains** ($\approx 4$ boosts, $+8$ stat points) and 5.83 talents over 10 rolls.

| 2d6 Roll | Probability | Feature / Ability |
| :---: | :---: | :--- |
| **2** | 2.8% | **Divine Wildcard:** $+2$ to ANY attribute OR choose any option on this table. |
| **3–6** | 38.9% | **Divine Favor & Smite:** $+1$ to Priest Spellcasting checks/DC OR $+1$ to Turn Undead checks and damage vs Undead. |
| **7–9** | **41.7%** | **Holy Attribute Boost:** $+2$ to **Wisdom**, **Strength**, or **Charisma** (or $+1$ to two of these stats). |
| **10–11** | 13.9% | **Sacred Protection & Healing:** $+1$ AC while wearing medium/heavy armor OR $+1$ to HP restored by healing spells. |
| **12** | 2.8% | **Divine Wildcard:** $+2$ to ANY attribute OR choose any option on this table. |

#### Wizard Talent Table (8 Open Slots $\rightarrow$ Target 4 Stat Gains)
* **Stat Band:** Rolls **6–9** (50.0% chance). Yields **EXACTLY 4.00 stat gains** ($+8$ stat points) and 4 talents over 8 rolls.

| 2d6 Roll | Probability | Feature / Ability |
| :---: | :---: | :--- |
| **2** | 2.8% | **Arcane Wildcard:** $+2$ to ANY attribute OR choose any option on this table. |
| **3–5** | 25.0% | **Arcane Memory & Mastery:** $+1$ to Spellcasting checks/DC OR learn 1 additional spell of any known spell level. |
| **6–9** | **50.0%** | **Mental Attribute Boost:** $+2$ to **Intelligence**, **Wisdom**, or **Charisma** (or $+1$ to two of these stats). |
| **10–11** | 13.9% | **Wards & Metamagic:** $+1$ to Saving Throws vs Magical Effects OR $+10$ ft range on non-touch spells. |
| **12** | 2.8% | **Arcane Wildcard:** $+2$ to ANY attribute OR choose any option on this table. |

---

### 6.3 Master Level 1–20 Progression Chart

| Level | Thief | Fighter | Priest | Magic User |
| :---: | :--- | :--- | :--- | :--- |
| **1** | **Advantage on thievery skills, 2x backstab, +4 to backstab** | **2 weapon masteries, fighting style** | **Turn Undead, 1st lv spells** | **1st lv spells** |
| **2** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table |
| **3** | Roll 2d6 Talent Table | **Improved Critical** | **2nd lv spells** | **2nd lv spells** |
| **4** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table |
| **5** | **3x backstab** | Roll 2d6 Talent Table | **3rd lv spells** | **3rd lv spells** |
| **6** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table |
| **7** | Roll 2d6 Talent Table | **2nd attack with mastery** | **4th lv spells** | **4th lv spells** |
| **8** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table |
| **9** | **4x backstab** | Roll 2d6 Talent Table | **5th lv spells** | **5th lv spells** |
| **10** | **2nd Storey Work** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | **Make Scrolls and Potions** |
| **11** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | **6th lv spells** | **6th lv spells** |
| **12** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table |
| **13** | **5x backstab** | Roll 2d6 Talent Table | **7th lv spells** | **7th lv spells** |
| **14** | **Use Magic Device** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | **Make Permanent Items** |
| **15** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | **Divine Intervention** | **8th lv spells** |
| **16** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table |
| **17** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table | **9th lv spells** |
| **18** | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table | Roll 2d6 Talent Table |
| **19** | **Epic Boon or Thieve's Reflexes** | **Epic Boon or Supreme Critical** | **Epic Boon or Corona of Light** | **Epic Boon or Circle Magic** |
| **20** | **Elusive** | **Defy Death** | **Greater Divine Intervention** | Roll 2d6 Talent Table |

