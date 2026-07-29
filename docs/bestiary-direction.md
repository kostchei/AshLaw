# Bestiary Direction

## Identity

The bestiary should feel like a dangerous first-edition AD&D monster manual
translated into Ashen Ultima's world. Encounters favor strange anatomy,
unfair-looking abilities with readable tells, ecological specificity, and
concrete counterplay. The art may evoke the period's inked fantasy and compact
pixel silhouettes, but must be original.

Names and descriptions below are working design references. Creatures strongly
associated with D&D should receive original shipping names, silhouettes, prose,
and mechanics rather than copying published art or stat blocks.

## Rendering constraints

- A human is approximately 20–28 pixels tall at 320×200.
- Small creatures occupy one footpad; large creatures may cover several
  logical tiles while retaining a single depth anchor at their foremost feet.
- Every creature needs a readable idle silhouette, movement cycle, attack tell,
  impact frame, death sequence, and corpse or remains state.
- Dark creatures still need a value-separated rim or moving internal detail;
  they must not disappear into the desaturated world.
- Reserve the strongest chroma for eyes, venom, lightning, spell effects,
  exposed wounds, and treasure-bearing anatomy.

## First encounter families

### Many-Eyed Tyrant

Reference touchstone: beholder.

A levitating armored orb whose lesser eyes track different targets. Its central
gaze suppresses magic inside a visible cone; individual eye stalks telegraph
short ray attacks before firing. Breaking line of sight, attacking from
different elevations, and severing stalks provide counterplay.

Visual identity: dull ivory shell, wet black joints, one low-saturation central
iris, and brief high-chroma ray colors. It should look ancient and anatomical,
not like a direct reproduction of D&D's beholder art.

### Umbral Fiend

Reference touchstone: shadow demon.

A planar predator that moves rapidly between connected areas of darkness,
becomes substantial when attacking, and is weakened or pinned by strong light.
Its long wind-up grapple drains warmth and temporarily reduces carrying
capacity or action speed.

Visual identity: a flattened, angular silhouette with a cold violet-grey rim.
The attack frame briefly reveals bone-like structures inside the shadow.

### Storm Crawler

Reference touchstone: behir.

A long, many-legged cavern predator that wraps around pillars and narrow
passages. It alternates between a constricting rush and a clearly aimed line of
lightning. Grounded metal, water, and cover change the lightning hazard.

Visual identity: slate hide, six pairs of short grasping legs, a pale charged
throat, and branching blue-white light only during the attack tell.

### Catoblepas

Reference touchstone: the mythological catoblepas and its early fantasy-game
interpretations.

A swamp scavenger with a heavy downward-hanging head. It spreads a sickening
miasma, sweeps nearby attackers with its tail, and slowly raises its head before
a lethal gaze. Turning away, breaking sight, interrupting the lift, or using
reflective cover prevents the gaze from becoming arbitrary instant death.

Visual identity: mud-grey buffalo mass, sparse black mane, dragging reptilian
neck, and small fever-red eyes visible only during the gaze tell.

## Mechanical contract

Each monster receives:

1. A basic attack resolved through the shared combat curve.
2. One signature ability that changes positioning or inventory/resource use.
3. A visible or audible tell before its most dangerous effect.
4. At least one discoverable weakness involving equipment, terrain, light,
   posture, facing, or damage type.
5. A corpse/remains container and a small, creature-specific loot table.
6. An ecology entry explaining where it lives and what nearby evidence warns
   the player.

This keeps monsters compatible with the same deterministic simulation,
container looting, and depth-sorted world used by the current vertical slice.
