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

The Slashing Critical Table uses a single-vector **1–18 lookup index** derived from the raw d20 roll's units digit plus a **Tier Modifier**:

$$\text{Lookup Index} = (\text{raw d20} \bmod 10) + \text{Tier Modifier}$$

* **Tier A (Minor)**: $+0$ (Indices 1–10)
* **Tier B (Moderate)**: $+2$ (Indices 3–12)
* **Tier C (Severe)**: $+4$ (Indices 5–14)
* **Tier D (Lethal)**: $+6$ (Indices 7–16)
* **Tier E (Catastrophic)**: $+8$ (Indices 9–18)

| Index | Mastery | Concussion Damage & Bleed | Full Narrative & Mechanical Outcome |
| :---: | :---: | :---: | :--- |
| **1** | **Graze** | **+Ability Mod** | **Glancing Strike.** Deals extra damage equal to attacker's STR/DEX modifier. |
| **2** | **Vex** | **+0 hits** (1 bleed/rd) | **Minor calf wound.** Attacker gains Advantage on next attack roll against target before end of next turn. |
| **3** | **Topple** | **+3 hits** | **Blow to upper leg.** Target must make a CON save (DC = 8 + PB + Mod) or fall Prone. (If no leg armor: 1 bleed/rd). |
| **4** | **Sap** | **+2 hits** (1 bleed/rd) | **Minor chest wound.** Target has Disadvantage on next attack roll before start of your next turn. |
| **5** | **Vex** | **+3 hits** (1 bleed/rd) | **Forearm slash.** Attacker gains Advantage on next attack roll; target drops held item. |
| **6** | **Topple** | **+4 hits** (1 bleed/rd) | **Medium thigh wound.** Target must make a CON save (DC = 8 + PB + Mod) or fall Prone, and is Stunned for 1 round. |
| **7** | **Sap** | **+5 hits** (1 bleed/rd) | **Slash weapon arm.** Target has Disadvantage on all weapon attack rolls (arm muscle & tendon damaged). |
| **8** | **Cleave** | **+6 hits** | **Sweeping arc strike.** Deals +6 hits to target, and attacker can make a secondary attack roll against an enemy within 5 ft (dealing weapon damage without ability mod on hit). |
| **9** | **Sap (Heavy)** | **+8 hits** | **Head strike.** Stunned 2 rounds, target has Disadvantage on next attack roll. (If no helm: knocked out 1 hr). |
| **10** | **Topple (Severe)** | **+10 hits** (5 bleed/rd) | **Sever lower leg.** Target automatically falls Prone and is Stunned for 2 rounds. |
| **11** | — | **+8 hits** (4 bleed/rd) | **Major abdominal wound.** Target has Disadvantage on all physical checks & saves, Stunned 3 rounds. |
| **12** | — | **+10 hits** (5 bleed/rd) | **Sever weapon arm.** Weapon arm useless/severed, target is knocked Prone and Stunned 4 rounds. |
| **13** | — | **+10 hits** (6 bleed/rd) | **Sever hand.** Hand severed, target falls Prone and is Stunned 4 rounds. |
| **14** | — | **+12 hits** (8 bleed/rd) | **Sever spine.** Collapses immediately, lower body paralyzed permanently. |
| **15** | — | **Dying (0 HP)** | **Arterial throat gash.** Target immediately drops to 0 HP, falls Prone, and is Dying (begins rolling Death Saves on the Death Clock). |
| **16** | — | **Dying (0 HP)** | **Sever shoulder & collarbone.** Arm destroyed. Target drops to 0 HP, is Unconscious and Dying (begins rolling Death Saves). |
| **17** | — | **Instant Death** | **Decapitation.** Head severed from body. Target dies instantly. |
| **18** | — | **Instant Death** | **Cleaved in two.** Torso severed from shoulder to hip. Target dies instantly. |

---

### 3.2 CT-1: Crush Critical Table (Maces, Warhammers, Clubs, Mauls)

The Crush Critical Table uses a single-vector **1–18 lookup index** derived from the raw d20 roll's units digit plus a **Tier Modifier**:

$$\text{Lookup Index} = (\text{raw d20} \bmod 10) + \text{Tier Modifier}$$

* **Tier A (Minor)**: $+0$ (Indices 1–10)
* **Tier B (Moderate)**: $+2$ (Indices 3–12)
* **Tier C (Severe)**: $+4$ (Indices 5–14)
* **Tier D (Lethal)**: $+6$ (Indices 7–16)
* **Tier E (Catastrophic)**: $+8$ (Indices 9–18)

| Index | Mastery Pulled | Concussion Damage & Bleed | Full Narrative & Mechanical Outcome |
| :---: | :---: | :---: | :--- |
| **1** | **Graze** | **+Ability Mod** | **Glancing Strike.** Deals extra damage equal to attacker's STR/DEX modifier. |
| **2** | **Slow** | **+3 hits** | **Minor rib contusion.** Attacker gains Advantage on next attack roll against target before end of next turn. |
| **3** | **Sap** | **+4 hits** | **Forearm blow.** Attacker gains Advantage on next attack roll; target drops weapon. |
| **4** | **Push** | **+5 hits** | **Chest blow.** Knocked back 10 feet. |
| **5** | **Topple** | **+6 hits** | **Thigh blow.** Target must make a CON save (DC = 8 + PB + Mod) or fall Prone, and is Stunned for 1 round. |
| **6** | **Sap (Heavy)** | **+8 hits** | **Blow to shield shoulder breaks shield.** If no shield: shoulder broken, arm useless. |
| **7** | **Topple (Heavy)** | **+10 hits** | **Blow breaks bone in leg.** Target automatically falls Prone and is Stunned for 2 rounds. |
| **8** | **Push & Sap** | **+10 hits** | **Blow breaks weapon arm.** Weapon arm useless, target drops weapon and is Stunned for 2 rounds. |
| **9** | **Topple (Severe)** | **+12 hits** | **Shatter knee.** Target falls Prone and is Stunned for 3 rounds. |
| **10** | **Skull Concussion** | **+15 hits** | **Blow to side of head.** Stunned for 3 rounds. (If no helm: knocked out for 4 hours). |
| **11** | — | **+15 hits** | **Shatter elbow in weapon arm.** Weapon arm useless, target drops weapon and is Stunned for 4 rounds. |
| **12** | — | **+18 hits** | **Blow breaks hip.** Target falls Prone and is Stunned for 4 rounds. |
| **13** | — | **+20 hits** | **Blast to chest sends ribcage through lungs.** Target falls Prone and is Stunned for 4 rounds. |
| **14** | — | **Dying (0 HP)** | **Neck strike crushes throat.** Target immediately reaches 0 HP, falls Prone, and is Dying (begins Death Saves). |
| **15** | — | **Dying (0 HP)** | **Blow to side crushes chest cavity.** Target reaches 0 HP, falls Prone, and is Dying (begins Death Saves). |
| **16** | — | **Dying (0 HP)** | **Crushed skull and vertebrae.** Target reaches 0 HP, is unconscious and Dying (begins Death Saves). |
| **17** | — | **Instant Death** | **Skull crushed.** Bone driven into brain. Target dies instantly. |
| **18** | — | **Instant Death** | **Heart crushed.** Chest cavity collapsed. Target dies instantly. |

---

### 3.3 CT-3: Puncture Critical Table (Daggers, Spears, Arrows, Rapiers)

The Puncture Critical Table uses a single-vector **1–18 lookup index** derived from the raw d20 roll's units digit plus a **Tier Modifier**:

$$\text{Lookup Index} = (\text{raw d20} \bmod 10) + \text{Tier Modifier}$$

* **Tier A (Minor)**: $+0$ (Indices 1–10)
* **Tier B (Moderate)**: $+2$ (Indices 3–12)
* **Tier C (Severe)**: $+4$ (Indices 5–14)
* **Tier D (Lethal)**: $+6$ (Indices 7–16)
* **Tier E (Catastrophic)**: $+8$ (Indices 9–18)

| Index | Mastery Pulled | Concussion Damage & Bleed | Full Narrative & Mechanical Outcome |
| :---: | :---: | :---: | :--- |
| **1** | **Graze** | **+Ability Mod** | **Glancing Strike.** Deals extra damage equal to attacker's STR/DEX modifier. |
| **2** | **Slow** | **+0 hits** (1 bleed/rd) | **Minor calf wound.** Reduces target's Speed by 10 ft until start of next turn. Attacker gains Advantage on next attack. |
| **3** | **Vex** | **+2 hits** | **Forearm stab.** Attacker gains Advantage on next attack roll against target before end of next turn; target drops weapon. |
| **4** | **Sap** | **+2 hits** (1 bleed/rd) | **Minor chest wound.** Target has Disadvantage on next attack roll before start of your next turn. |
| **5** | **Topple** | **+3 hits** (1 bleed/rd) | **Leg tendon thrust.** Target must make a CON save (DC = 8 + PB + Mod) or fall Prone. |
| **6** | **Push** | **+4 hits** | **Heavy chest thrust.** Target is pushed 10 feet back. |
| **7** | **Vex (Heavy)** | **+5 hits** (1 bleed/rd) | **Weapon arm puncture.** Attacker gains Advantage on next attack; target has Disadvantage on all weapon attack rolls. |
| **8** | **Slow & Sap** | **+5 hits** (2 bleed/rd) | **Knee tendon strike.** Target has Disadvantage on next attack roll. |
| **9** | **Sap (Heavy)** | **+8 hits** | **Head strike.** Stunned 2 rounds, target has Disadvantage on next attack roll. (If no helm: KO 1 hr). |
| **10** | **Topple (Severe)** | **+10 hits** (4 bleed/rd) | **Deep leg puncture.** Target automatically falls Prone and is Stunned for 2 rounds. |
| **11** | — | **+8 hits** (5 bleed/rd) | **Major abdominal wound.** Target has Disadvantage on all physical checks & saves, Stunned 3 rounds. |
| **12** | — | **+10 hits** (6 bleed/rd) | **Spinal puncture.** Target is Stunned for 4 rounds, leg useless. |
| **13** | — | **+10 hits** (6 bleed/rd) | **Nailed in lower back.** Target falls Prone and is Stunned for 4 rounds. |
| **14** | — | **+12 hits** (8 bleed/rd) | **Severed artery.** Target falls Prone and is Stunned for 4 rounds. |
| **15** | — | **Dying (0 HP)** | **Kidney puncture.** Target immediately drops to 0 HP, falls Prone, and is Dying (begins Death Saves on Death Clock). |
| **16** | — | **Dying (0 HP)** | **Subclavian artery puncture.** Target drops to 0 HP, is Unconscious and Dying (begins Death Saves). |
| **17** | — | **Instant Death** | **Eye puncture.** Spear/arrow pierces eye into brain. Target dies instantly. |
| **18** | — | **Instant Death** | **Heart pierced.** Heart punctured through chest. Target dies instantly. |

---

### 3.4 CT-4: Unbalancing Critical Table (Tackles, Trips, Shield Slams)

The Unbalancing Critical Table uses a single-vector **1–18 lookup index** derived from the raw d20 roll's units digit plus a **Tier Modifier**:

$$\text{Lookup Index} = (\text{raw d20} \bmod 10) + \text{Tier Modifier}$$

* **Tier A (Minor)**: $+0$ (Indices 1–10)
* **Tier B (Moderate)**: $+2$ (Indices 3–12)
* **Tier C (Severe)**: $+4$ (Indices 5–14)
* **Tier D (Lethal)**: $+6$ (Indices 7–16)
* **Tier E (Catastrophic)**: $+8$ (Indices 9–18)

| Index | Mastery Pulled | Concussion Damage & Bleed | Full Narrative & Mechanical Outcome |
| :---: | :---: | :---: | :--- |
| **1** | **Graze** | **+Ability Mod** | **Glancing Strike.** Deals extra damage equal to attacker's STR/DEX modifier. |
| **2** | **Slow** | **+2 hits** | **Minor leg strike.** Attacker gains Advantage on next attack roll against target. |
| **3** | **Push** | **+3 hits** | **Chest strike.** Knocked back 10 feet. |
| **4** | **Disarm & Sap** | **+4 hits** | **Strike to weapon arm.** Attacker gains Advantage on next attack; target drops weapon. |
| **5** | **Topple** | **+5 hits** | **Leg strike.** Target must make a CON save (DC = 8 + PB + Mod) or fall Prone, and is Stunned for 1 round. |
| **6** | **Push Heavy** | **+6 hits** | **Shield arm strike.** Shield torn away, target knocked back 10 feet. |
| **7** | **Topple Heavy** | **+8 hits** | **Blow to upper leg.** Target automatically falls Prone and is Stunned for 2 rounds. |
| **8** | **Head Blow** | **+10 hits** | **Blow to side of head.** Stunned for 2 rounds. (If no helm: knocked out for 1 hour). |
| **9** | **Push & Topple** | **+12 hits** | **Blow to chest.** Knocked back 10 feet, target falls Prone and is Stunned for 3 rounds. |
| **10** | **Side Blow / Ribs** | **+12 hits** | **Blow to side.** Ribs broken, target falls Prone and is Stunned for 3 rounds. |
| **11** | — | **+15 hits** | **Blow to leg.** Leg broken, target falls Prone and is Stunned for 4 rounds. |
| **12** | — | **+15 hits** | **Blow to weapon arm.** Arm broken, target drops weapon, falls Prone and is Stunned for 4 rounds. |
| **13** | — | **+18 hits** | **Blow to head.** Target falls Prone and is Stunned for 4 rounds. |
| **14** | — | **Dying (0 HP)** | **Knocked off feet, head hits stone.** Target immediately reaches 0 HP, falls Prone, and is Dying (begins Death Saves). |
| **15** | — | **Dying (0 HP)** | **Violent trip onto back.** Target reaches 0 HP, falls Prone, and is Dying (begins Death Saves). |
| **16** | — | **Dying (0 HP)** | **Spinal fracture on impact.** Target reaches 0 HP, is unconscious and Dying (begins Death Saves). |
| **17** | — | **Instant Death** | **Neck broken on impact.** Target dies instantly. |
| **18** | — | **Instant Death** | **Skull crushed on impact.** Target dies instantly. |

---

### 3.5 CT-5: Grappling Critical Table (Holds, Joint Locks, Entanglements)

The Grappling Critical Table uses a single-vector **1–18 lookup index**:

$$\text{Lookup Index} = (\text{raw d20} \bmod 10) + \text{Tier Modifier}$$

| Index | Mastery Pulled | Concussion Damage & Bleed | Full Narrative & Mechanical Outcome |
| :---: | :---: | :---: | :--- |
| **1** | **Graze** | **+Ability Mod** | **Opportunity lost.** Glancing hold. Extra damage equal to attacker's STR/DEX modifier. |
| **2** | **Slow** | **+2 hits** | **Off-balance hold.** Reduces target's Speed by 10 ft until start of next turn. |
| **3** | **Vex / Disarm** | **+3 hits** | **Wrist grasp.** Attacker gains Advantage on next attack; target drops weapon. |
| **4** | **Sap** | **+3 hits** | **Spun about.** Target has Disadvantage on next attack roll before start of next turn. |
| **5** | **Topple / Grapple** | **+4 hits** | **Shield arm entangled.** Target is Grappled (Speed 0) until freed. |
| **6** | **Disarm & Sap** | **+5 hits** | **Weapon arm grasped.** Target is Disarmed (drops weapon) and has Disadvantage on next attack. |
| **7** | **Topple Heavy** | **+6 hits** | **Leg entangled.** Target automatically falls Prone and is Stunned for 1 round. |
| **8** | **Restrained** | **+8 hits** | **Both arms entangled & pinned to chest.** Target is Restrained (Speed 0, Disadvantage on attacks). |
| **9** | **Head Grapple** | **+10 hits** | **Head grappled.** Target is Stunned for 2 rounds. (If no helm: KO for 1 hour). |
| **10** | **Chest Crush** | **+10 hits** | **Chest grasped.** Ribs broken, target is Stunned for 3 rounds. |
| **11** | — | **+12 hits** | **Foot entangled, stumble & fall.** Weapon arm broken, target drops weapon, falls Prone, Stunned 3 rounds. |
| **12** | — | **+15 hits** | **Completely entangled & immobilized.** Target falls Prone, is Restrained and Stunned for 4 rounds. |
| **13** | — | **+20 hits** | **Full body tumble.** Arm & ankle broken, target falls Prone and is Stunned for 4 rounds. |
| **14** | — | **Dying (0 HP)** | **Vicious neck crank.** Target immediately reaches 0 HP, falls Prone, and is Dying (begins Death Saves). |
| **15** | — | **Dying (0 HP)** | **Throat crushed in hold.** Target reaches 0 HP, falls Prone, and is Dying (begins Death Saves). |
| **16** | — | **Dying (0 HP)** | **Head slammed into stone.** Target reaches 0 HP, is unconscious and Dying (begins Death Saves). |
| **18** | — | **Instant Death** | **Spine crushed in submission hold.** Target dies instantly. |

---

### 3.6 CT-6: Heat Critical Table (Fire, Plasma, Searing Magic)

The Heat Critical Table uses a single-vector **1–18 lookup index**:

$$\text{Lookup Index} = (\text{raw d20} \bmod 10) + \text{Tier Modifier}$$

| Index | Elemental Effect | Damage & Bleed | Full Narrative & Mechanical Outcome |
| :---: | :---: | :---: | :--- |
| **1** | **Graze** | **+Ability Mod** | **Glancing Strike.** Hot air blast. Extra damage equal to attacker's spellcasting modifier. |
| **2** | **Heat Flash** | **+3 hits** | **Strong heat wave.** Attacker gains Advantage on next attack roll against target. |
| **3** | **Smoke Blind** | **+6 hits** (1 burn/rd) | **Minor burns & hot smoke.** Target is Blinded until end of its next turn. |
| **4** | **Ignite** | **+8 hits** (2 burn/rd) | **Clothing catches on fire.** Takes 2 fire damage per round until extinguished. |
| **5** | **Fiery Blast** | **+10 hits** | **Fiery blast.** Target must make a CON save (DC = 8 + PB + Mod) or fall Prone. |
| **6** | **Back Engulfed** | **+12 hits** (2 burn/rd) | **Fire engulfs back.** Target falls Prone. |
| **7** | **Shield Fried** | **+12 hits** | **Shield arm scorched.** Shield broken (if no shield: arm useless). |
| **8** | **Leg Scorched** | **+15 hits** (3 burn/rd) | **Blast to leg.** Leg useless, target falls Prone. |
| **9** | **Head Scorched** | **+15 hits** | **Scorching head blast.** Target is Blinded and has Disadvantage on next attack. |
| **10** | **Chest Blast** | **+18 hits** | **Blast to chest.** Chest armor broken, target falls Prone. |
| **11** | **Organic Destruct** | **+20 hits** (4 burn/rd) | **Fire engulfs body.** Target falls Prone and is Stunned for 1 round. |
| **12** | **Massive Burn** | **+22 hits** (5 burn/rd) | **Severe body incineration.** Target falls Prone. |
| **13** | **Cerebral Shock** | **Dying (0 HP)** | **Blast to head causes massive shock.** Target immediately reaches 0 HP, falls Prone, and is Dying. |
| **14** | **Nerve Failure** | **Dying (0 HP)** | **Body engulfed in searing flame.** Target reaches 0 HP, falls Prone, and is Dying. |
| **15** | **Charred Organs** | **Dying (0 HP)** | **Internal organs charred by heat.** Target reaches 0 HP, is unconscious and Dying. |
| **16** | **Midsection Melt** | **Dying (0 HP)** | **Midsection incinerated.** Target reaches 0 HP, is unconscious and Dying. |
| **17** | **Instant Death** | **Instant Death** | **Reduced to ash.** Body incinerated. Target dies instantly. |
| **18** | **Instant Death** | **Instant Death** | **Vaporized instantly by fiery inferno.** Target dies instantly. |

---

### 3.7 CT-7: Cold Critical Table (Frost, Ice, Cryo Magic)

| Index | Elemental Effect | Damage & Bleed | Full Narrative & Mechanical Outcome |
| :---: | :---: | :---: | :--- |
| **1** | **Graze** | **+Ability Mod** | **Glancing Strike.** Cool breeze. Extra damage equal to attacker's spellcasting modifier. |
| **2** | **Slow** | **+3 hits** | **Cold blast.** Target's Speed is reduced by 10 ft until start of next turn. |
| **3** | **Frost Burn** | **+5 hits** (1 frost/rd) | **Frosty burn.** Target has Disadvantage on next attack roll. |
| **4** | **Mild Frostbite** | **+6 hits** (2 frost/rd) | **Mild frostbite.** Target's Speed is reduced by 10 ft. |
| **5** | **Ice Blast Back** | **+8 hits** (2 frost/rd) | **Cold strike to back.** Target must make a CON save (DC = 8 + PB + Mod) or fall Prone. |
| **6** | **Foot Freeze** | **+8 hits** | **Low frost blast.** Target drops held item. |
| **7** | **Leg Frostbite** | **+10 hits** (3 frost/rd) | **Strike to leg.** Lower leg useless, target falls Prone. |
| **8** | **Hands Frozen** | **+12 hits** | **Blast freezes hands.** Target drops weapon. |
| **9** | **Neck Freeze** | **+12 hits** | **Strike to neck and collar area.** Target has Disadvantage on next attack. |
| **10** | **Pelvis Shatter** | **+15 hits** (4 frost/rd) | **Thigh iced & bone broken.** Target falls Prone. |
| **11** | **Chest Freeze** | **+18 hits** (5 frost/rd) | **Icy blast to upper chest.** Target falls Prone. |
| **12** | **Hypothermia** | **+20 hits** | **Severe body freeze.** Target falls Prone. |
| **13** | **Organ Freeze** | **Dying (0 HP)** | **Heart and lungs frozen.** Target immediately reaches 0 HP, falls Prone, and is Dying. |
| **14** | **Pelvic Shatter Death** | **Dying (0 HP)** | **Pelvis shattered by deep freeze.** Target reaches 0 HP, falls Prone, and is Dying. |
| **15** | **Hypothermic Collapse** | **Dying (0 HP)** | **Core body temperature plummets.** Target reaches 0 HP, is unconscious and Dying. |
| **16** | **Frozen Statue** | **Dying (0 HP)** | **Frozen solid into a statue.** Target reaches 0 HP, is unconscious and Dying. |
| **17** | **Instant Death** | **Instant Death** | **Frozen solid and shatters into pieces.** Target dies instantly. |
| **18** | **Instant Death** | **Instant Death** | **Brain and heart flash frozen.** Target dies instantly. |

---

### 3.8 CT-8: Electricity Critical Table (Lightning, Shock, Arc Magic)

| Index | Elemental Effect | Damage & Bleed | Full Narrative & Mechanical Outcome |
| :---: | :---: | :---: | :--- |
| **1** | **Graze** | **+Ability Mod** | **Glancing Strike.** Static charge. Extra damage equal to attacker's spellcasting modifier. |
| **2** | **Light Charge** | **+3 hits** | **Light electrical charge.** Attacker gains Advantage on next attack roll against target. |
| **3** | **Arc Flash** | **+4 hits** | **Arc flash explosion.** Target is Blinded until end of next turn. |
| **4** | **Medium Charge** | **+6 hits** | **Medium electrical charge.** Target has Disadvantage on next attack roll. |
| **5** | **Heavy Charge** | **+9 hits** | **Heavy electrical charge.** Target must make a CON save (DC = 8 + PB + Mod) or fall Prone. |
| **6** | **Shield Shock** | **+12 hits** | **Strike to shield arm.** Target drops shield. |
| **7** | **Arm Spasm** | **+12 hits** (2 shock/rd) | **Strike to weapon arm.** Weapon arm useless, target drops weapon. |
| **8** | **Facial Flash** | **+14 hits** | **Strike to face.** Target is Blinded and falls Prone. |
| **9** | **Chest Discharge** | **+15 hits** | **Chest strike.** Target has Disadvantage on next attack roll. |
| **10** | **Abdominal Shock** | **+18 hits** (4 shock/rd) | **Abdomen strike.** Target falls Prone. |
| **11** | **Arc Chain** | **+20 hits** (5 shock/rd) | **Massive chest discharge.** Target falls Prone. |
| **12** | **Neural Overload** | **+22 hits** | **Neural system shock.** Target falls Prone. |
| **13** | **Nervous System Collapse** | **Dying (0 HP)** | **Permeated by electricity.** Target immediately reaches 0 HP, falls Prone, and is Dying. |
| **14** | **Neural Failure** | **Dying (0 HP)** | **Brain falls victim to massive neural shock.** Target reaches 0 HP, falls Prone, and is Dying. |
| **15** | **Cardiac Arrest** | **Dying (0 HP)** | **Heart and lungs destroyed by high voltage.** Target reaches 0 HP, is unconscious and Dying. |
| **16** | **Abdominal Sever** | **Dying (0 HP)** | **Abdominal cavity severed by electrical arc.** Target reaches 0 HP, is unconscious and Dying. |
| **17** | **Instant Death** | **Instant Death** | **Heart disintegrated by superconductor arc.** Target dies instantly. |
| **18** | **Instant Death** | **Instant Death** | **Cell structure disrupted.** Body turned to ash. Target dies instantly. |

---

### 3.9 CT-9: Impact Critical Table (Force, Kinetic Blasts, Sonic Magic)

| Index | Elemental Effect | Damage & Bleed | Full Narrative & Mechanical Outcome |
| :---: | :---: | :---: | :--- |
| **1** | **Graze** | **+Ability Mod** | **Glancing Strike.** Grazing blast. Extra damage equal to attacker's spellcasting modifier. |
| **2** | **Stagger** | **+5 hits** | **Grazing blast.** Attacker gains Advantage on next attack roll against target. |
| **3** | **Spun About** | **+8 hits** | **Strike to shoulder spun about.** Knocked back 10 feet. |
| **4** | **Knockdown** | **+8 hits** | **Strike to leg.** Target must make a CON save (DC = 8 + PB + Mod) or fall Prone. |
| **5** | **Side Blast** | **+10 hits** | **Staggered by side blast.** Knocked back 10 feet. |
| **6** | **Shield Smashed** | **+10 hits** | **Blast to shield arm.** Shield broken (if no shield: shoulder broken). |
| **7** | **Collar Blast** | **+12 hits** | **Blast to collar area.** Target drops weapon. |
| **8** | **Torn Muscle** | **+15 hits** | **Blow to upper leg.** Leg useless, target falls Prone. |
| **9** | **Knee Dislocation** | **+15 hits** | **Knee dislocated.** Target falls Prone. |
| **10** | **Jaw Fracture** | **+15 hits** | **Blow to jaw.** Target has Disadvantage on next attack roll. |
| **11** | **Abdomen Blast** | **+18 hits** | **Blast to abdomen.** Target falls Prone. |
| **12** | **Arms Broken** | **+20 hits** | **Both arms broken.** Target drops weapon, falls Prone. |
| **13** | **Kidney Rupture** | **Dying (0 HP)** | **Blow to side drives rib into kidneys.** Target immediately reaches 0 HP, falls Prone, and is Dying. |
| **14** | **Organ Destruction** | **Dying (0 HP)** | **Internal organs destroyed by shockwave.** Target reaches 0 HP, falls Prone, and is Dying. |
| **15** | **Chest Collapse** | **Dying (0 HP)** | **Chest cavity crushed by impact.** Target reaches 0 HP, is unconscious and Dying. |
| **16** | **Skull Fractured** | **Dying (0 HP)** | **Skull fractured into pieces.** Target reaches 0 HP, is unconscious and Dying. |
| **17** | **Instant Death** | **Instant Death** | **Skull shattered into particles.** Target dies instantly. |
| **18** | **Instant Death** | **Instant Death** | **Entire skeleton pulverized by kinetic force.** Target dies instantly. |
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
