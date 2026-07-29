# D20 Single-Roll MERP Critical Hit System

This document defines the mathematical framework and table structure for integrating a MERP-inspired (*Middle-earth Role Playing*) critical hit system into a deterministic single-roll d20 engine.

---

## 1. Overview & Design Philosophy

Classic MERP combat is renowned for its visceral narrative trauma (bleeding, stuns, severed limbs, organ damage) rather than simple hit-point attrition. In traditional MERP, this required two separate d100 rolls and multiple table lookups per hit.

This system adapts MERP's critical severity tables (**A** through **E**) to a **single d20 roll**, utilizing:
1. **Net Roll Margin** to determine the **Critical Severity Tier (A–E)**.
2. **Raw d20 Units Digit** to select **1 of 10 specific trauma outcomes** with a uniform 10% probability per outcome.

---

## 2. Core Resolution Workflow

```
+-----------------------------------------------------------------------------------+
|                            SINGLE-ROLL COMBAT FLOW                                |
|                                                                                   |
|  Attacker Rolls 1d20 + Attack Mod - Defense Mod  --->  Net Roll                  |
|                                                          |                        |
|                  +---------------------------------------+                        |
|                  |                                       |                        |
|                  v                                       v                        |
|      1. Standard Damage (Hits)               2. Critical Severity (Tier)          |
|   z = max(0, Net Roll - Damage Origin)       Crit Tier = (Net Roll - Base Crit)   |
|   Hits = Linear*z + Quadratic*z^2                         / Crit Interval          |
|                                                          |                        |
|                                                          v                        |
|                                              3. Trauma Result (1-10)              |
|                                              Index = raw d20 % 10 (0=10)          |
+-----------------------------------------------------------------------------------+
```

### Formula Summary

1. **Net Roll Calculation:**
   $$\text{Net Roll} = \text{d20} + \text{Attack Mod} - \text{Defense Mod}$$

2. **Standard Damage (Hits):**
   $$z = \max(0, \text{Net Roll} - \text{Damage Origin})$$
   $$\text{Hits} = \max\left(0, \lfloor \text{Multiplier}z + \text{Quadratic}z^2 \rfloor\right)$$

3. **Critical Severity Tier:**
   $$\text{Crit Tier} = \left\lfloor \frac{\text{Net Roll} - \text{Base Crit Threshold}}{\text{Crit Interval}} \right\rfloor$$

   | Crit Tier Value | Severity Grade | Trauma Severity Description |
   | :---: | :---: | :--- |
   | **0** | **Tier A** | Minor Trauma (minor bleeding, brief stuns, slight action penalties) |
   | **1** | **Tier B** | Moderate Trauma (moderate bleeding, 1-2 rnd stuns, sprains, muscle tears) |
   | **2** | **Tier C** | Severe Trauma (heavy bleeding, major stuns, broken bones, severe penalties) |
   | **3** | **Tier D** | Lethal Trauma (massive bleeding, destroyed limbs, collapsed lungs, high death risk) |
   | **4+** | **Tier E** | Catastrophic Trauma (decapitation, crushed skull, pierced heart, instant death) |

4. **Injury Sub-Result Indexing (1 of 10):**
   Take the **units digit** of the raw d20 roll (`raw d20 % 10`):

   | Raw d20 Die Roll | Units Digit (`d20 % 10`) | Selected Sub-Result Index | Probability |
   | :---: | :---: | :---: | :---: |
   | 1 or 11 | 1 | **Result 1** | 10% |
   | 2 or 12 | 2 | **Result 2** | 10% |
   | 3 or 13 | 3 | **Result 3** | 10% |
   | 4 or 14 | 4 | **Result 4** | 10% |
   | 5 or 15 | 5 | **Result 5** | 10% |
   | 6 or 16 | 6 | **Result 6** | 10% |
   | 7 or 17 | 7 | **Result 7** | 10% |
   | 8 or 18 | 8 | **Result 8** | 10% |
   | 9 or 19 | 9 | **Result 9** | 10% |
   | 10 or 20 | 0 | **Result 10** | 10% |

---

### 2.1 d20 Activity & Injury Conversion Rules

To translate MERP's percentile activity penalties (`-5 to -75 activity`) and permanent trauma into modern d20 / D&D mechanics:

| MERP Activity / Trauma | d20 / D&D Equivalent | Gameplay Rule & Mechanical Effect |
| :--- | :--- | :--- |
| **$-5 \text{ to } -15 \text{ Activity}$** | **Flat $-1$ or $-2$ Penalty** | Add a $-1$ (for $-5\%$) or $-2$ (for $-10\%$ to $-15\%$) penalty to attack rolls, AC, or checks. |
| **$-25 \text{ to } -75 \text{ Activity}$** | **Temporary Disadvantage** | Target suffers **Disadvantage** on attack rolls, physical checks, and DEX/STR saving throws for the specified duration. |
| **Permanent Bone Fracture / Limb Ruin** | **Stonetop-style Stat Impairment** | Gives permanent **Disadvantage on 1 or 2 relevant Ability Scores**: |
| • *Severed Tendon / Shattered Leg* | **Impaired DEX & STR (Lower Body)** | Permanent **Disadvantage on DEX & STR checks**, movement speed halved. |
| • *Shattered Arm / Shoulder Ruin* | **Impaired STR & Attack Rolls** | Permanent **Disadvantage on STR checks & Attacks** using that arm. |
| • *Eye Destroyed / Head Trauma* | **Impaired WIS & INT** | Permanent **Disadvantage on WIS (Perception) & Concentration**. |
| • *Crushed Ribs / Punctured Lung* | **Impaired CON** | Permanent **Disadvantage on CON checks**, HP max reduced by 25%. |

### 2.2 d20 Standard Condition Mapping (Prone, Restrained, Incapacitated, Stunned, Unconscious)

MERP status descriptions map cleanly onto official d20 / 5e conditions in order of escalating severity:

| MERP Status Term | d20 / D&D Condition | Rules & Mechanical Effect in Play |
| :--- | :--- | :--- |
| **Knocked Down / Fell Down / Stumble** | **`Prone`** | Target falls prone. Melee attacks against have Advantage, ranged have Disadvantage; costs half movement to stand. |
| **Pinned / Constricted / Wrapped** | **`Restrained`** | Speed = 0. Target cannot move, attacks against have Advantage, target's attacks have Disadvantage, DEX saves at Disadvantage. |
| **Stunned 1–2 Rounds / Bell Rung** | **`Incapacitated`** | Target cannot take Actions or Reactions for the duration. (Can still move unless Prone). |
| **Heavy Stun (3+ Rounds) / Concussion** | **`Stunned`** | Target is Incapacitated, speed = 0, automatically fails STR/DEX saving throws, attack rolls against have Advantage. |
| **Coma / Unconscious / Passed Out** | **`Unconscious`** | Target falls Prone, drops held items, is Incapacitated, automatically fails STR/DEX saves; melee hits within 5ft are auto-crits. |
| **Bleed Damage & Instant Death** | **Bleeding & Death Saves** | Bleed ticks at start of target's turn. Dropping to 0 HP initiates standard d20 Death Saving Throws (unless "Dies Instantly"). |

---

## 3. Official MERP Critical Trauma Tables (CT-1 through CT-4)

### 3.1 CT-2: Slashing Critical Table (Swords, Axes, Bladed Weapons)

| Index | Tier A (Minor) | Tier B (Moderate) | Tier C (Severe) | Tier D (Lethal) | Tier E (Catastrophic) |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **1** | Weak strike yields no extra damage. +0 hits. | Weak strike yields no extra damage. +0 hits. | Weak strike yields no extra damage. +0 hits. | Minor calf wound. 1 hit per round. | Blow to upper leg. +5 hits. If no leg armor: +3 hits & 2 hits/rnd. |
| **2** | Minor calf wound. 1 hit per round. | Minor calf wound. 1 hit per round. | Minor calf wound. 1 hit per round. | Blow to upper leg. +5 hits. If no leg armor: +3 hits & 2 hits/rnd. | Minor chest wound. +3 hits. 1 hit per round. -5 to activity. |
| **3** | Minor calf wound. 1 hit per round. | Blow to upper leg. +5 hits. If no leg armor: +3 hits & 2 hits/rnd. | Blow to upper leg. +5 hits. If no leg armor: +3 hits & 2 hits/rnd. | Minor chest wound. +3 hits. 1 hit per round. -5 to activity. | Minor chest wound. +3 hits. 1 hit per round. -5 to activity. |
| **4** | Blow to upper leg. +5 hits. If no leg armor: +3 hits & 2 hits/rnd. | Minor chest wound. +3 hits. 1 hit per round. -5 to activity. | Minor chest wound. +3 hits. 1 hit per round. -5 to activity. | Minor chest wound. +3 hits. 1 hit per round. -5 to activity. | Minor forearm wound. +4 hits. 2 hits per round. Stunned 1 round. |
| **5** | Minor chest wound. +3 hits. 1 hit per round. -5 to activity. | Minor chest wound. +3 hits. 1 hit per round. -5 to activity. | Minor chest wound. +3 hits. 1 hit per round. -5 to activity. | Minor forearm wound. +4 hits. 2 hits per round. Stunned 1 round. | Medium thigh wound. +6 hits. 1 hit per round. -10 to activity. Stunned 2 rounds. |
| **6** | Minor chest wound. +3 hits. 1 hit per round. -5 to activity. | Minor forearm wound. +4 hits. 2 hits per round. Stunned 1 round. | Minor forearm wound. +4 hits. 2 hits per round. Stunned 1 round. | Medium thigh wound. +6 hits. 1 hit per round. -10 to activity. Stunned 2 rounds. | Slash weapon arm. +10 hits. 1 hit per round. If no arm armor: muscle & tendon damage, arm useless. |
| **7** | Minor forearm wound. +4 hits. 2 hits per round. Stunned 1 round. | Minor forearm wound. +4 hits. 2 hits per round. Stunned 1 round. | Medium thigh wound. +6 hits. 1 hit per round. -10 to activity. Stunned 2 rounds. | Slash weapon arm. +10 hits. 1 hit per round. If no arm armor: muscle & tendon damage, arm useless. | Destroys one eye. +10 hits. Stunned for 30 rounds. |
| **8** | Minor forearm wound. +4 hits. 2 hits per round. Stunned 1 round. | Medium thigh wound. +6 hits. 1 hit per round. -10 to activity. Stunned 2 rounds. | Slash weapon arm. +10 hits. 1 hit per round. If no arm armor: muscle & tendon damage, arm useless. | Slash weapon arm. +10 hits. 1 hit per round. If no arm armor: muscle & tendon damage, arm useless. | Knocked out for 6 hours with a strike to side of head. +15 hits. If no helm: dies instantly. |
| **9** | Medium thigh wound. +6 hits. 1 hit per round. -10 to activity. Stunned 2 rounds. | Medium thigh wound. +6 hits. 1 hit per round. -10 to activity. Stunned 2 rounds. | Slash weapon arm. +10 hits. 1 hit per round. If no arm armor: muscle & tendon damage, arm useless. | Knocked out for 6 hours with a strike to side of head. +15 hits. If no helm: dies instantly. | Major abdominal wound. +10 hits. 8 hits per round. -10 to activity. Stunned for 4 rounds. |
| **10** | Medium thigh wound. +6 hits. 1 hit per round. -10 to activity. Stunned 2 rounds. | Slash weapon arm. +10 hits. 1 hit per round. If no arm armor: muscle & tendon damage, arm useless. | Knocked out for 6 hours with a strike to side of head. +15 hits. If no helm: dies instantly. | Major abdominal wound. +10 hits. 8 hits per round. -10 to activity. Stunned for 4 rounds. | Sever hand. 12 hits per round. Knocked down and stunned for 6 rounds. |

---

### 3.2 CT-1: Crush Critical Table (Maces, Warhammers, Clubs)

| Index | Tier A (Minor) | Tier B (Moderate) | Tier C (Severe) | Tier D (Lethal) | Tier E (Catastrophic) |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **1** | Weak grip. No extra damage. +0 hits. | Weak grip. No extra damage. +0 hits. | Weak grip. No extra damage. +0 hits. | Minor fracture of ribs. +5 hits. -5 to activity. | Blow to side. +4 hits. -40 to activity for 1 round. |
| **2** | Minor fracture of ribs. +5 hits. -5 to activity. | Minor fracture of ribs. +5 hits. -5 to activity. | Minor fracture of ribs. +5 hits. -5 to activity. | Blow to side. +4 hits. -40 to activity for 1 round. | Blow to forearm. +5 hits. If no arm armor, stunned 1 round. |
| **3** | Minor fracture of ribs. +5 hits. -5 to activity. | Blow to side. +4 hits. -40 to activity for 1 round. | Blow to side. +4 hits. -40 to activity for 1 round. | Blow to forearm. +5 hits. If no arm armor, stunned 1 round. | Blow to forearm. +5 hits. If no arm armor, stunned 1 round. |
| **4** | Blow to side. +4 hits. -40 to activity for 1 round. | Blow to forearm. +5 hits. If no arm armor, stunned 1 round. | Blow to forearm. +5 hits. If no arm armor, stunned 1 round. | Blow to forearm. +5 hits. If no arm armor, stunned 1 round. | Blow to shield shoulder breaks shield. If no shield: shoulder broken, arm useless. |
| **5** | Blow to forearm. +5 hits. If no arm armor, stunned 1 round. | Blow to forearm. +5 hits. If no arm armor, stunned 1 round. | Blow to forearm. +5 hits. If no arm armor, stunned 1 round. | Blow to shield shoulder breaks shield. If no shield: shoulder broken, arm useless. | Blow breaks bone in leg. +12 hits. -40 to activity. Stunned 2 rounds. |
| **6** | Blow to forearm. +5 hits. If no arm armor, stunned 1 round. | Blow to shield shoulder breaks shield. If no shield: shoulder broken, arm useless. | Blow to shield shoulder breaks shield. If no shield: shoulder broken, arm useless. | Blow breaks bone in leg. +12 hits. -40 to activity. Stunned 2 rounds. | Blow to weapon arm. +8 hits. Stunned 2 rounds. If no arm armor: tendon damaged, arm broken & useless. |
| **7** | Blow to shield shoulder breaks shield. If no shield: shoulder broken, arm useless. | Blow to shield shoulder breaks shield. If no shield: shoulder broken, arm useless. | Blow breaks bone in leg. +12 hits. -40 to activity. Stunned 2 rounds. | Blow to weapon arm. +8 hits. Stunned 2 rounds. If no arm armor: tendon damaged, arm broken & useless. | Shatter knee. +9 hits. -60 to activity. Knocked down and stunned for 3 rounds. |
| **8** | Blow to shield shoulder breaks shield. If no shield: shoulder broken, arm useless. | Blow breaks bone in leg. +12 hits. -40 to activity. Stunned 2 rounds. | Blow to weapon arm. +8 hits. Stunned 2 rounds. If no arm armor: tendon damaged, arm broken & useless. | Blow to weapon arm. +8 hits. Stunned 2 rounds. If no arm armor: tendon damaged, arm broken & useless. | Unconscious for 4 hours due to blow to side of head. If no helm: skull crushed. +20 hits. |
| **9** | Blow breaks bone in leg. +12 hits. -40 to activity. Stunned 2 rounds. | Blow breaks bone in leg. +12 hits. -40 to activity. Stunned 2 rounds. | Blow to weapon arm. +8 hits. Stunned 2 rounds. If no arm armor: tendon damaged, arm broken & useless. | Unconscious for 4 hours due to blow to side of head. If no helm: skull crushed. +20 hits. | Blow breaks hip. +15 hits. -75 to activity. Knocked down and stunned 3 rounds. |
| **10** | Blow breaks bone in leg. +12 hits. -40 to activity. Stunned 2 rounds. | Blow to weapon arm. +8 hits. Stunned 2 rounds. If no arm armor: tendon damaged, arm broken & useless. | Unconscious for 4 hours due to blow to side of head. If no helm: skull crushed. +20 hits. | Blow breaks hip. +15 hits. -75 to activity. Knocked down and stunned 3 rounds. | Shatter elbow in weapon arm. Arm useless. Stunned 5 rounds. |

---

### 3.3 CT-3: Puncture Critical Table (Daggers, Spears, Arrows)

| Index | Tier A (Minor) | Tier B (Moderate) | Tier C (Severe) | Tier D (Lethal) | Tier E (Catastrophic) |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **1** | Glancing blow. No extra damage. | Glancing blow. No extra damage. | Glancing blow. No extra damage. | Glancing blow to side. +3 hits. | Thigh strike, +3 hits. If no leg armor: 3 hits per round. |
| **2** | Glancing blow to side. +3 hits. | Glancing blow to side. +3 hits. | Glancing blow to side. +3 hits. | Thigh strike, +3 hits. If no leg armor: 3 hits per round. | Minor forearm wound. +2 hits. If no arm armor: stunned 1 round. |
| **3** | Glancing blow to side. +3 hits. | Thigh strike, +3 hits. If no leg armor: 3 hits per round. | Thigh strike, +3 hits. If no leg armor: 3 hits per round. | Minor forearm wound. +2 hits. If no arm armor: stunned 1 round. | Minor forearm wound. +2 hits. If no arm armor: stunned 1 round. |
| **4** | Thigh strike, +3 hits. If no leg armor: 3 hits per round. | Minor forearm wound. +2 hits. If no arm armor: stunned 1 round. | Minor forearm wound. +2 hits. If no arm armor: stunned 1 round. | Minor forearm wound. +2 hits. If no arm armor: stunned 1 round. | Strike along side of chest. 1 hit per round. Stunned 1 round. |
| **5** | Minor forearm wound. +2 hits. If no arm armor: stunned 1 round. | Minor forearm wound. +2 hits. If no arm armor: stunned 1 round. | Minor forearm wound. +2 hits. If no arm armor: stunned 1 round. | Strike along side of chest. 1 hit per round. Stunned 1 round. | Strike to lower leg. Tendons torn. +3 hits. -25 to activity. Stunned 1 round. |
| **6** | Minor forearm wound. +2 hits. If no arm armor: stunned 1 round. | Strike along side of chest. 1 hit per round. Stunned 1 round. | Strike along side of chest. 1 hit per round. Stunned 1 round. | Strike to lower leg. Tendons torn. +3 hits. -25 to activity. Stunned 1 round. | Strike to weapon arm. +10 hits. If no arm armor: bone broken, stunned 3 rounds. |
| **7** | Strike along side of chest. 1 hit per round. Stunned 1 round. | Strike along side of chest. 1 hit per round. Stunned 1 round. | Strike to lower leg. Tendons torn. +3 hits. -25 to activity. Stunned 1 round. | Strike to weapon arm. +10 hits. If no arm armor: bone broken, stunned 3 rounds. | Strike through lower leg. Sever muscle. -50 to activity. Stunned 3 rounds. |
| **8** | Strike along side of chest. 1 hit per round. Stunned 1 round. | Strike to lower leg. Tendons torn. +3 hits. -25 to activity. Stunned 1 round. | Strike to weapon arm. +10 hits. If no arm armor: bone broken, stunned 3 rounds. | Strike to weapon arm. +10 hits. If no arm armor: bone broken, stunned 3 rounds. | Strike to side of head. Knocked out for 6 hours. +10 hits. If no helm: dies instantly. |
| **9** | Strike to lower leg. Tendons torn. +3 hits. -25 to activity. Stunned 1 round. | Strike to lower leg. Tendons torn. +3 hits. -25 to activity. Stunned 1 round. | Strike to weapon arm. +10 hits. If no arm armor: bone broken, stunned 3 rounds. | Strike to side of head. Knocked out for 6 hours. +10 hits. If no helm: dies instantly. | Major abdominal wound. +10 hits. 6 hits per round. -20 to activity. Stunned 4 rounds. |
| **10** | Strike to lower leg. Tendons torn. +3 hits. -25 to activity. Stunned 1 round. | Strike to weapon arm. +10 hits. If no arm armor: bone broken, stunned 3 rounds. | Strike to side of head. Knocked out for 6 hours. +10 hits. If no helm: dies instantly. | Major abdominal wound. +10 hits. 6 hits per round. -20 to activity. Stunned 4 rounds. | Strike through leg. Artery severed. Down and unconscious. 12 hits per round. |

---

### 3.4 CT-4: Unbalancing Critical Table (Tackles, Trips, Shield Slams)

| Index | Tier A (Minor) | Tier B (Moderate) | Tier C (Severe) | Tier D (Lethal) | Tier E (Catastrophic) |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **1** | Fairly weak. +0 hits. Zip. | Fairly weak. +0 hits. Zip. | Fairly weak. +0 hits. Zip. | Arm strike. +2 hits. -5 to activity for 2 rounds. | Leg strike. +4 hits. If no leg armor: stunned 1 round. |
| **2** | Arm strike. +2 hits. -5 to activity for 2 rounds. | Arm strike. +2 hits. -5 to activity for 2 rounds. | Arm strike. +2 hits. -5 to activity for 2 rounds. | Leg strike. +4 hits. If no leg armor: stunned 1 round. | Chest strike. Knocked back 3 feet. +5 hits. -10 to activity for 2 rnds. |
| **3** | Arm strike. +2 hits. -5 to activity for 2 rounds. | Leg strike. +4 hits. If no leg armor: stunned 1 round. | Leg strike. +4 hits. If no leg armor: stunned 1 round. | Chest strike. Knocked back 3 feet. +5 hits. -10 to activity for 2 rnds. | Chest strike. Knocked back 3 feet. +5 hits. -10 to activity for 2 rnds. |
| **4** | Leg strike. +4 hits. If no leg armor: stunned 1 round. | Chest strike. Knocked back 3 feet. +5 hits. -10 to activity for 2 rnds. | Chest strike. Knocked back 3 feet. +5 hits. -10 to activity for 2 rnds. | Chest strike. Knocked back 3 feet. +5 hits. -10 to activity for 2 rnds. | Blow to shield arm. +5 hits. Shield torn away. If no shield: +8 hits and stunned 2 rounds. |
| **5** | Chest strike. Knocked back 3 feet. +5 hits. -10 to activity for 2 rnds. | Chest strike. Knocked back 3 feet. +5 hits. -10 to activity for 2 rnds. | Chest strike. Knocked back 3 feet. +5 hits. -10 to activity for 2 rnds. | Blow to shield arm. +5 hits. Shield torn away. If no shield: +8 hits and stunned 2 rounds. | Elbow strike. Forearm numbed. +8 hits. Drop weapon. -10 to activity for 10 rounds. |
| **6** | Chest strike. Knocked back 3 feet. +5 hits. -10 to activity for 2 rnds. | Blow to shield arm. +5 hits. Shield torn away. If no shield: +8 hits and stunned 2 rounds. | Blow to shield arm. +5 hits. Shield torn away. If no shield: +8 hits and stunned 2 rounds. | Elbow strike. Forearm numbed. +8 hits. Drop weapon. -10 to activity for 10 rounds. | Shot to side. Knocked 5 feet sideways. Drop anything carried in hands. Stunned 3 rounds. |
| **7** | Blow to shield arm. +5 hits. Shield torn away. If no shield: +8 hits and stunned 2 rounds. | Blow to shield arm. +5 hits. Shield torn away. If no shield: +8 hits and stunned 2 rounds. | Elbow strike. Forearm numbed. +8 hits. Drop weapon. -10 to activity for 10 rounds. | Shot to side. Knocked 5 feet sideways. Drop anything carried in hands. Stunned 3 rounds. | Side strike. Stumble ungracefully to an embarrassing prone position. Stunned 6 rounds. |
| **8** | Blow to shield arm. +5 hits. Shield torn away. If no shield: +8 hits and stunned 2 rounds. | Elbow strike. Forearm numbed. +8 hits. Drop weapon. -10 to activity for 10 rounds. | Shot to side. Knocked 5 feet sideways. Drop anything carried in hands. Stunned 3 rounds. | Shot to side. Knocked 5 feet sideways. Drop anything carried in hands. Stunned 3 rounds. | Hard head strike. Knocked back 10' and stunned 6 rounds. If no helm: unconscious for 24 hours. |
| **9** | Elbow strike. Forearm numbed. +8 hits. Drop weapon. -10 to activity for 10 rounds. | Elbow strike. Forearm numbed. +8 hits. Drop weapon. -10 to activity for 10 rounds. | Shot to side. Knocked 5 feet sideways. Drop anything carried in hands. Stunned 3 rounds. | Hard head strike. Knocked back 10' and stunned 6 rounds. If no helm: unconscious for 24 hours. | Blow breaks leg. +12 hits. -50 to activity. Stunned 1 round. |
| **10** | Elbow strike. Forearm numbed. +8 hits. Drop weapon. -10 to activity for 10 rounds. | Shot to side. Knocked 5 feet sideways. Drop anything carried in hands. Stunned 3 rounds. | Hard head strike. Knocked back 10' and stunned 6 rounds. If no helm: unconscious for 24 hours. | Blow breaks leg. +12 hits. -50 to activity. Stunned 1 round. | Great side shot. Knocked down and sideways 5'. Lower leg broken. Stunned 7 rounds. -40 to activity. |

---

## 4. Resolution Walkthrough Example

### Scenario
An attacker swings a **Greataxe (2-Handed Slashing)** at a defender wearing **Chain Armor**:
* **Weapon vs Armor Profile (from JSON data):**
  * Hit Threshold: `11`
  * Multiplier: `2.0`
  * Base Crit (A): `19`
  * Crit Interval: `2`
* **Attack Modifiers:** Attack Mod = `+6`, Defense Mod = `+2`
* **Dice Roll:** Raw d20 roll = **17**

### Calculations

1. **Net Roll:**
   $$\text{Net Roll} = 17 + 6 - 2 = \mathbf{21}$$

2. **Standard Damage (Hits):**
   $$\text{Hits} = \lfloor (21 - 11) \times 2.0 \rfloor = \lfloor 10 \times 2.0 \rfloor = \mathbf{20 \text{ Hits}}$$

3. **Critical Severity Tier:**
   $$\text{Crit Tier} = \left\lfloor \frac{21 - 19}{2} \right\rfloor = \left\lfloor \frac{2}{2} \right\rfloor = \mathbf{1 \text{ (Tier B Severity)}}$$

4. **Specific Trauma Sub-Result (1 of 10):**
   * Raw d20 roll = **17**
   * Units digit = `17 % 10` = **7**
   * Look up **Index 7** under **Slashing — Tier B**:
     * *"Scalp torn. +3 bleed/rd, Stun 1 rd, blinded 1 rd (blood in eyes)."*

### Final Combat Outcome
The defender receives **20 HP damage (Hits)**, is **stunned for 1 round**, takes **3 bleeding damage per round**, and is **blinded for 1 round** due to blood pouring into their eyes.

---

---

## 5. Special Hazards, Weapon Categories & Monster Mechanics

All special hazards, creature attacks, and spell types resolve using the **exact same core equations** defined in Section 2, referencing their specific entry in [combat_system_data.json](../data/combat_system_data.json).

### 5.1 Falling & Environmental Crush Hazards
Falling damage and environmental crushing use the **Concussion (Crush)** Trauma Table with `environmental_fall_crush` parameters from JSON:
* **Attack Mod:** Height / Kinetic Momentum bonus (e.g. +1 per 10ft fallen).
* **Hits & Crit Tier:** Calculated strictly via standard Net Roll formulas.

---

### 5.2 Polearms & Missile Weapons
* **Polearms (Halberd, Pike, 2H Spear):** Uses **AT-3: 2-Handed Weapons** parameters. Fired as thrust $\rightarrow$ **Puncturing** Table; swung $\rightarrow$ **Slashing** Table.
* **Missile Weapons (Longbow, Heavy Crossbow):** Uses **AT-4: Missile Weapons** parameters and the **Puncturing** Trauma Table.

---

### 5.3 Tentacles, Squids & Creature Attacks (Grapple / Constrict)
Monster tentacles, claws, and unarmed grapples use **AT-5: Animal Attacks** parameters and the **Grapple / Constriction Trauma Table**.

#### Grapple / Constriction Trauma Table (Squid & Beast Attacks)

| Index | Tier A (Minor) | Tier B (Moderate) | Tier C (Severe) | Tier D (Lethal) | Tier E (Catastrophic) |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **1** | Tentacle wraps arm. -5 act. | Arm pinned. Drop weapon, Stun 1 rd. | Arm crushed. Stun 2 rds, arm broken, -30 act. | Arm torn from socket. +8 bleed/rd, Stun 4 rds, arm ruined. | Body pulled under water. Drown in 2 rds unless freed. |
| **2** | Leg grabbed. -10 move. | Leg pinned. Stun 1 rd, fall down, -20 move. | Knee joint pop. Stun 2 rds, down, -40 move. | Leg crushed & broken. Stun 4 rds, +4 bleed/rd, down. | Target dragged into deep water. Drowns in 1 round. |
| **3** | Suction cup tears skin. +1 bleed/rd. | Suction cup tears flesh. +3 bleed/rd, Stun 1 rd. | Deep muscle tear from suction. +5 bleed/rd, Stun 2 rds. | Arterial suction tear. +9 bleed/rd, Stun 4 rds, unconscious 3 rds. | Tentacle wraps neck & drowns. Asphyxiated instantly. |
| **4** | Torso squeezed. Stun 1 rd. | Ribs compressed. Stun 1 rd, -15 act. | 2 Ribs cracked by grip. Stun 2 rds, +2 bleed/rd, -30 act. | Ribcage crushed flat. Stun 4 rds, +6 bleed/rd, dies in 3 rds. | Torso snapped in two. Instant death. |
| **5** | Mouth/face covered. Cannot speak 1 rd. | Face wrapped. Stun 1 rd, blinded & muffled. | Windpipe compressed. Stun 2 rds, cannot breathe, -40 act. | Throat crushed. Stun 4 rds, suffocates in 3 rds. | Head popped off torso. Instant death. |
| **6** | Shield caught. Drop shield. | Weapon arm constricted. Stun 1 rd, drop weapon. | Both arms pinned. Stun 3 rds, cannot attack or spellcast. | Collarbone & shoulder crushed. Stun 4 rds, upper torso ruined. | Bisected by tentacle squeeze. Instant death. |
| **7** | Ankle pulled. Fall prone. | Pulled off balance. Stun 1 rd, prone, -20 act. | Dragged 10ft toward mouth. Stun 2 rds, prone. | Dragged into beak/mouth. Bite attack triggers next rd. | Swallowed whole by giant squid/beak. Instant death. |
| **8** | Glancing tentacle slap. Stun 1 rd. | Heavy tentacle whip. Stun 2 rds, -10 act. | Tentacle smash to head. Stun 3 rds, concussion, -25 act. | Skull fracture from slam. Stun 5 rds, coma, dies in 1 hr. | Brain crushed by tentacle slam. Instant death. |
| **9** | Waist grabbed. -5 move. | Lifted off feet. Stun 1 rd, floating, -15 act. | Spine compressed. Stun 3 rds, +2 bleed/rd, -35 act. | Spine snapped at waist. Stun 4 rds, lower body paralyzed. | Spine pulverized. Instant death. |
| **10** | Wrist gripped. Drop secondary. | Wrist constricted. Stun 1 rd, drop item, -10 act. | Hand bones crushed. Stun 2 rds, hand useless. | Elbow & wrist crushed. Stun 4 rds, arm ruined. | Chest crushed & dragged under. Instant death. |

---

### 5.4 Large & Super-Large Creature Size Scaling
Reflecting MERP's rules for monster scale (Trolls, Giant Squids, Dragons):
* **Attacking a Large Creature:** Shift Critical Severity **down by 1 Tier** (e.g., Tier C $\rightarrow$ Tier B; Tier A ignored).
* **Attacking a Super-Large Creature:** Shift Critical Severity **down by 2 Tiers** (e.g., Tier E $\rightarrow$ Tier C; Tiers A and B ignored).
* **Being Attacked BY a Large Creature:** Shift Critical Severity **UP by +1 Tier** (Tier A $\rightarrow$ Tier B).
* **Being Attacked BY a Super-Large Creature:** Shift Critical Severity **UP by +2 Tiers** (Tier B $\rightarrow$ Tier D).

---

## 6. Elemental Spells: Bolts, Balls & Elemental Criticals

All elemental spells resolve using the **standard d20 Net Roll equations**:

* **AT-7: Spell Bolts** (Fire Bolt, Ice Bolt, Shock Bolt) $\rightarrow$ Uses **Heat**, **Cold**, or **Electrical** Trauma Table.
* **AT-8: Spell Balls** (Fireball, Coldball, Lightning Ball) $\rightarrow$ Uses **Heat**, **Cold**, or **Electrical** Trauma Table.


---

### 6.2 Elemental Critical Tables

#### Heat Criticals (Fire Bolts & Fireballs)

| Index | Tier A (Minor) | Tier B (Moderate) | Tier C (Severe) | Tier D (Lethal) | Tier E (Catastrophic) |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **1** | Hair singed. Stun 1 rd. | Face scorched. Stun 1 rd, -10 act. | Face blistered. Stun 2 rds, blinded 1 rd, -20 act. | Third-degree facial burns. Stun 4 rds, permanently blinded in 1 eye. | Head engulfed in flame. Skull burned through, instant death. |
| **2** | Tunic catches spark. +1 bleed/rd (burn). | Clothes ignite. +2 bleed/rd, Stun 1 rd. | Armor heats up. +4 bleed/rd, Stun 2 rds, -25 act. | Metal armor fuses to skin. +8 bleed/rd, Stun 4 rds, -50 act. | Metal plate melts into chest. Internal organs incinerated, instant death. |
| **3** | Hand singed. Drop secondary. | Hand burned. Drop weapon, Stun 1 rd. | Hand blistered severe. Stun 2 rds, hand useless 24 hrs. | Tendons burned through in arm. Stun 4 rds, arm ruined. | Both arms incinerated to bone. Shock death in 2 rds. |
| **4** | Eyebrows singed. -5 act. | Smoke in eyes. Stun 1 rd, blinded 1 rd. | Cornea scorched. Stun 3 rds, -30 act, vision impaired. | Lungs scorched by hot air. Stun 4 rds, suffocates in 4 rds. | Lungs incinerated. Instant death. |
| **5** | Foot scorched. -5 move. | Boot charred. Stun 1 rd, -15 move. | Leg blistered deep. Stun 2 rds, -35 move. | Leg muscle charred to bone. Stun 4 rds, leg ruined. | Lower body incinerated. Instant death. |
| **6** | Shield scorched. Shield defense -1. | Shield ignites/warps. Drop shield, Stun 1 rd. | Shield wood ash / metal scalds. Arm burned, -20 act. | Torso third-degree burns. +7 bleed/rd, Stun 4 rds, coma. | Torso incinerated to skeleton. Instant death. |
| **7** | Shoulder singed. -5 act. | Shoulder burned. Stun 1 rd, -10 act. | Collarbone heat fracture. Stun 2 rds, arm limp. | Neck burned to trachea. +9 bleed/rd, Stun 4 rds, dies in 3 rds. | Throat & neck vaporized. Instant death. |
| **8** | Flash of flame. Stun 1 rd. | Severe shock. Stun 2 rds, -15 act. | Heat shock. Stun 3 rds, unconscious 1d6 minutes. | Massive thermal shock. Stun 5 rds, coma, dies in 1 hr. | Entire body turned to ash. Instant disintegration. |
| **9** | Back singed. +1 bleed/rd. | Back scorched. +2 bleed/rd, Stun 1 rd. | Spine area burned deep. +4 bleed/rd, Stun 3 rds, -30 act. | Nerves burned along spine. Stun 4 rds, paralyzed down. | Spine burned through. Instant death. |
| **10** | Arm singed. Stun 1 rd. | Bicep burned. +2 bleed/rd, Stun 1 rd. | Forearm charred. +4 bleed/rd, Stun 2 rds, -20 act. | Chest burst by steam/heat. +10 bleed/rd, dies in 2 rds. | Heart boiled in chest cavity. Instant death. |

---

#### Cold Criticals (Ice Bolts & Coldballs)

| Index | Tier A (Minor) | Tier B (Moderate) | Tier C (Severe) | Tier D (Lethal) | Tier E (Catastrophic) |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **1** | Frost on face. Stun 1 rd. | Shivering fit. Stun 1 rd, -10 act. | Severe hypothermia. Stun 2 rds, -25 act. | Deep tissue freezing. Stun 4 rds, unconscious 1d6 hrs. | Solid ice block. Target frozen solid, instant death. |
| **2** | Fingers numb. -5 act. | Frostbite on fingers. Drop weapon, Stun 1 rd. | Hand frozen stiff. Stun 2 rds, hand unusable. | Hand shatters on impact. Stun 4 rds, hand lost permanently. | Arm freezes solid & shatters. Shock death in 3 rds. |
| **3** | Toes numb. -5 move. | Feet frozen. Stun 1 rd, -20 move. | Ankle joint frozen rigid. Stun 2 rds, down, -40 move. | Leg muscle frozen brittle. Stun 4 rds, leg breaks on step. | Both legs freeze & shatter. Instant shock death. |
| **4** | Cold air inhaled. Stun 1 rd. | Breath caught. Stun 2 rds, -10 act. | Trachea frost-damaged. Stun 3 rds, unable to speak, -30 act. | Lungs filled with ice crystals. Stun 4 rds, suffocates 3 rds. | Lungs frozen solid. Instant death. |
| **5** | Arm chilled. Stun 1 rd. | Bicep frostbitten. Stun 1 rd, -15 act. | Elbow frozen solid. Stun 2 rds, arm locked at angle. | Arm frozen through. Stun 4 rds, arm breaks off. | Torso frozen through. Heart stops, instant death. |
| **6** | Shield iced over. -5 act. | Shield frozen to arm. Drop secondary, Stun 1 rd. | Armor metal supercooled. Stun 2 rds, +3 bleed/rd (frost-burn). | Armor freezes to skin. Stun 4 rds, severe frost necrosis. | Chest cavity frozen solid. Instant death. |
| **7** | Scalp chilled. Stun 1 rd. | Frost shock to head. Stun 2 rds, -15 act. | Ear freezes & snaps off. Stun 3 rds, -20 act. | Eye frozen in socket. Stun 4 rds, permanent loss of eye. | Brain frozen solid. Instant death. |
| **8** | Shiver. -5 act. | Muscle spasms from cold. Stun 1 rd, -15 act. | Blood vessel freezing. Stun 3 rds, +3 bleed/rd (internal). | Arteries frozen shut. Stun 4 rds, cardiac arrest in 2 rds. | Heart freezes mid-beat. Instant death. |
| **9** | Back chilled. +1 bleed/rd. | Back muscles frozen. Stun 1 rd, -15 act. | Spine supercooled. Stun 3 rds, nerve paralysis 1 hr. | Spinal fluid frozen. Stun 5 rds, permanent paralysis. | Spine shatters like glass. Instant death. |
| **10** | Knees chilled. -10 move. | Knee stiffened. Stun 1 rd, -15 move. | Kneecap frozen & cracked. Stun 2 rds, down, -40 move. | Lower body completely frozen. Stun 4 rds, down permanently. | Entire body shattered to ice shards. Instant death. |

---

#### Electrical Criticals (Shock Bolts & Lightning Balls)

| Index | Tier A (Minor) | Tier B (Moderate) | Tier C (Severe) | Tier D (Lethal) | Tier E (Catastrophic) |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **1** | Static shock. Stun 1 rd. | Violent muscle spasm. Drop weapon, Stun 1 rd. | Seizure fit. Stun 3 rds, down, drop all items. | Full body convulsion. Stun 5 rds, unconscious 1 hour. | Brain fried by arc. Instant death. |
| **2** | Arm tingle. -5 act. | Arm nerve shocked. Drop weapon, Stun 1 rd, -15 act. | Arm paralyzed 1d6 rds. Stun 2 rds, arm useless. | Nerve cluster destroyed in arm. Stun 4 rds, arm paralyzed permanently. | Both arms vaporized by arc. Shock death in 1 rd. |
| **3** | Leg shock. -10 move. | Leg spasm. Stun 1 rd, fall prone. | Leg paralyzed 1d6 rds. Stun 2 rds, down, -40 move. | Nerve system in leg burned. Stun 4 rds, permanent limp (-50 move). | Lower half exploded by steam/arc. Instant death. |
| **4** | Flash blinding. Blind 1 rd. | Flash bang in head. Stun 2 rds, deafened 3 rds. | Retinas burned. Stun 3 rds, blinded 1d6 hours. | Optic nerves destroyed. Stun 4 rds, permanently blind. | Skull burst by internal steam. Instant death. |
| **5** | Chest zapped. Stun 1 rd. | Arrhythmia pulse. Stun 2 rds, -15 act. | Heart fibrillating. Stun 3 rds, -30 act, collapses. | Cardiac arrest. Stun 4 rds, dies in 3 rds without CPR/magic. | Heart exploded by voltage. Instant death. |
| **6** | Shield shocks hand. Drop shield. | Metal armor conducts shock. Stun 2 rds, -10 act. | Metal armor superheats & arcs. Stun 3 rds, +4 bleed/rd (arc burns). | Metal plate melts to torso from arc. Stun 5 rds, dies in 2 rds. | Entire torso carbonized. Instant death. |
| **7** | Jaw tingle. Stun 1 rd. | Jaw locks up. Cannot speak/cast 2 rds. | Teeth shattered by arc. Stun 3 rds, -20 act. | Throat muscles paralyzed. Stun 4 rds, asphyxiates in 3 rds. | Head carbonized to ash. Instant death. |
| **8** | Hair stands up. Stun 1 rd. | Violent jolt. Stun 2 rds, drop all items. | Severe electrical burns. +4 bleed/rd (arc), Stun 3 rds. | Internal organs cooked by arc. +10 bleed/rd, dies in 2 rds. | Disintegrated into smoking ash. Instant death. |
| **9** | Spine tingle. -5 act. | Spine shock. Stun 1 rd, fall prone, -15 act. | Spinal cord shorted. Stun 3 rds, temporary paralysis 1 hour. | Spinal nerves burned through. Stun 5 rds, permanent paralysis. | Spine vaporized by arc. Instant death. |
| **10** | Foot shock. -5 move. | Ankle spasm. Stun 1 rd, -15 move. | Achilles tendon charred. Stun 2 rds, down, -50 move. | Central nervous system fried. Stun 5 rds, coma. | Complete nervous system collapse. Instant death. |
