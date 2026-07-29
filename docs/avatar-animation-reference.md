# Avatar Animation Reference

## Decision

Use the Flare hero as the first replacement for the code-drawn player.

Flare is the closest practical starting point found so far:

- it is an oblique, eight-direction action-RPG character rather than a
  front-facing overhead sprite;
- the hero is assembled from aligned body, head, clothing, armour, boots,
  gloves, weapons, and shields;
- the runtime sheets and their animation metadata are available as PNG and
  text files;
- the editable Blender source files are included;
- the supplied render is approximately 100-130 pixels high and can be reduced
  by half or quartered with the current 2x authoring transform to reach the
  Ashen Ultima target of about 50 logical pixels;
- the supplied actions cover stance, run, melee swing, block, hit, death,
  spellcasting, and shooting.

This is a technical choice as much as an asset-availability choice. Flare gives
us a reusable layered character pipeline. A single flattened walk sheet does
not.

The first integration should use the Flare male base in ordinary clothes, add
an Ashen Ultima backpack layer, reduce saturation, darken the value range, and
render it at approximately 100 pixels from feet to crown. Retain all eight
directions even if the current movement prototype initially exposes only four.

## What Ultima VIII actually animated

The original Avatar is shape 1 in `STATIC/U8SHAPES.FLX`. Its actions are
described by `STATIC/ANIM.DAT`. ScummVM's Ultima VIII animation decoder and
action enumeration were used to inspect those files; no original game art is
part of this repository.

The Avatar shape contains 1,550 frame records, of which 1,244 are non-empty.
Non-empty records range from 18-74 pixels wide and 12-66 pixels high, averaging
35.4 by 47.3 pixels. Ordinary standing and walking poses are commonly about
19-44 pixels wide and 47-51 pixels high. That directly supports the current
100-pixel actor target at 1280x800 when rendered at 2x relative to the original
Ultima VIII figure.

The Avatar has 53 populated actions. Nearly every action has eight directional
variants:

| Group | Action id and name | Frames per direction |
| --- | --- | ---: |
| Locomotion | 0 walk | 9 |
|  | 1 run | 9 |
|  | 2 stand | 1 |
|  | 8 advance | 7 |
|  | 9 retreat | 7 |
|  | 12 step | 5 |
| Jumping | 3 jump up | 21 |
|  | 10 running jump | 21 |
|  | 16 land | 7 |
|  | 17 jump | 18 |
|  | 18 air-walk jump | 18 |
| Weapon combat | 5 ready weapon | 13 |
|  | 6 unready weapon | 12 |
|  | 7 attack | 6 |
|  | 15 combat stand | 1 |
|  | 40 slow attack / block | 10 |
|  | 58 kick | 13 |
|  | 59 start block | 3 |
|  | 60 stop block | 5 |
| Reactions | 13 stumble backwards | 8 |
|  | 14 die | 12 |
|  | 42 keep balance | 9 |
|  | 44 fall backwards | 5 |
|  | 56 drown | 6 |
|  | 57 burn | 9 |
| Magic | 27 cast 1 | 13 |
|  | 28 cast 2 | 13 |
|  | 29 cast 3 | 15 |
|  | 30 cast 4 | 22 |
|  | 31 cast 5 | 20 |
| Climbing | 19 climb 16 | 7 |
|  | 20 climb 24 | 7 |
|  | 21 climb 32 | 8 |
|  | 22 climb 40 | 9 |
|  | 23 climb 48 | 11 |
|  | 24 climb 56 | 11 |
|  | 25 climb 64 | 15 |
|  | 26 climb 72 | 30 |
|  | 45 hang | 1 |
|  | 46 climb up | 10 |
| Posture and idle | 4 stand up | 19 |
|  | 11 shake head | 3 |
|  | 32 look left | 1 |
|  | 33 look right | 1 |
|  | 34 start kneeling | 6 |
|  | 35 kneel | 6 |
|  | 47 idle 1 | 1 |
|  | 48 idle 2 | 1 |
|  | 49 kneel 2 | 6 |
|  | 50 stop kneeling | 7 |
| NPC aliases | 36 NPC magic 1 | 1 |
|  | 37 NPC magic 2 | 1 |
|  | 38 NPC special | 1 |

The source enumeration also defines sitting, chair use, talking, work, and
other generic actor actions, but the Avatar has no populated frame lists for
action ids 39, 41, 43, 51-55, or 61-63.

The important lesson is frame reuse. The 53 action names are not 53 wholly
independent sprite productions. Related climb actions reuse overlapping frame
ranges; combat stance transitions share material; some NPC actions alias
standing frames; and action metadata controls looping, two-step movement,
attack timing, hanging, and repetition. Ashen Ultima should likewise separate
named gameplay actions from the smaller library of animation clips that
implements them.

## Ashen Ultima animation contract

All player and humanoid sprite sources should use:

- eight directions;
- a feet-based origin shared by every frame and equipment layer;
- separate action metadata rather than timing inferred from sheet position;
- optional body, head/hair, torso, legs, hands, footwear, backpack, main-hand,
  and off-hand layers;
- nearest-neighbour rendering into the 1280x800 presentation;
- action markers for the damage frame, projectile release, spell release,
  footstep, and interaction contact.

### First playable set

Implement these before expanding traversal:

1. stand / breathing idle;
2. walk and run;
3. combat stand plus ready and unready transitions;
4. one-handed attack;
5. block;
6. hit / stumble;
7. death and prone end-frame;
8. cast;
9. shoot;
10. kneel or short world-interaction pose.

Flare already supplies direct bases for 1-8. Walk can initially be derived by
retiming the run or produced from the included Blender rig. Kneel and explicit
ready/unready transitions need new renders.

### Second traversal set

Add jump, land, kick, fall backwards, balance, hang, climb-up, and variable
height climbing when elevation and traversal enter the playable slice. The
eight separate Ultima VIII climb heights should be represented by a smaller
set of reusable start, loop, and finish clips plus movement metadata.

### Later character acting

Add look, shake head, talk, sit, chair stand, work, drowning, burning, and
class-specific casting gestures only when the world systems use them.

## Candidate assets

### 1. Flare hero - adopt

Repository:
<https://github.com/flareteam/flare-game>

Relevant paths:

- `mods/fantasycore/images/avatar/`
- `mods/fantasycore/animations/avatar/`
- `mods/fantasycore/animations/hero.txt`
- `art_src/characters/base/human.blend`
- `art_src/characters/hero/`

The supplied clip layout is four stance frames, eight run frames, four swing
frames, two block frames, two hit frames, six death frames, four cast frames,
and four shoot frames, each in eight directions. Male, female, and dark-skinned
female bases are included, along with cloth, leather, chain, plate, mage
clothing, bows, staves, swords, axes, hammers, shields, and other equipment.

The art and data are CC-BY-SA 3.0-or-later. Keep attribution and the applicable
asset license beside imported or modified art. The repository includes its raw
Blender art sources, which is the decisive advantage for creating missing
poses and changing the visual treatment.

### 2. Flare NPC animated pack - useful expansion

<https://opengameart.org/content/flare-npcs-animated-pack-1>

This adds eight-direction peasants, nobles, old people, priests, a witch, and
other civilian silhouettes, with source files. It is a good way to populate a
first settlement without cloning the player body everywhere. It is not a
replacement for the combat Avatar.

### 3. Bleed's isometric archer - useful class/NPC reference

<https://opengameart.org/content/archer-bleeds-game-art>

This provides idle, walk, run, attack, and death in eight directions. It can
serve as an archer NPC or as reference for ranger and bow cadence, but it is
not modular enough to become the main player system.

### 4. Flare zombie - useful first undead

<https://opengameart.org/content/zombie-sprites>

This is a compatible eight-direction, 128-pixel-cell sheet with stance, lurch,
slam, bite, block, hit/death, and critical death. It can enter the bestiary
pipeline with the same reduction and palette treatment as the hero.

### 5. Large turnaround base - pose reference only

<https://opengameart.org/content/large-turnaround-sprite-base-animation>

This is a useful hand-pixelled eight-direction turnaround but not a complete
gameplay action set. Use it as a silhouette and perspective reference, not as
the player implementation.

### 6. Solaar isometric character template - walking fallback

<https://solaarnoble.itch.io/isotemplate>

This has eight-direction male, female, and child walking characters with an
Aseprite source. It is inexpensive and permits use in games but forbids asset
redistribution, so it is suitable for a private local prototype or a rendered
derivative inside the game, not for checking the raw pack into this public
repository.

## Assets rejected as the main Avatar

- Liberated Pixel Cup is a strong modular overhead character system, but its
  front-facing orthographic perspective does not match the Ultima VIII camera.
- Kenney's Roguelike Characters are excellent low-cost placeholders, but they
  are much smaller and do not supply the required directional action coverage.
- tiny tactical-RPG isometric packs work for map planning but make the Avatar a
  token rather than the tall, readable figure this project needs.
- a sheet with no stated reuse terms can be used as private visual reference,
  but should be side-loaded locally rather than committed. Non-commercial use
  does not by itself supply redistribution permission.

## Immediate implementation sequence

1. Import one Flare clothed hero source sheet and its metadata.
2. Map its 128-pixel cells into the project's compact authoring coordinates;
   the 4x world transform brings the source close to 1:1 in the logical
   framebuffer, avoiding a destructive reduction pass.
3. Draw the appropriate directional stance and run frame at the player's
   feet-based world position.
4. Add backpack, main-hand, and off-hand overlays using the same origin;
5. bind attack, hit, death, cast, and shoot clips to simulation events;
6. desaturate and relight the result to fit `art-direction.md`;
7. preserve the current code-drawn player as an explicit debug fallback.

## Reference implementation sources

- ScummVM Ultima VIII action enumeration:
  <https://github.com/scummvm/scummvm/blob/master/engines/ultima/ultima8/world/actors/animation.h>
- ScummVM Ultima VIII `ANIM.DAT` decoder:
  <https://github.com/scummvm/scummvm/blob/master/engines/ultima/ultima8/gfx/anim_dat.cpp>
- Flare game assets and source art:
  <https://github.com/flareteam/flare-game>
