# Art Direction: Ashen Ultima

## Visual target

Ashen Ultima uses a 640×400 logical presentation inspired by the readable, theatrical
interiors of *Ultima VIII*: oblique rooms, tall figures, strong silhouettes,
deep walls, dense props, and a restrained earth-tone palette. The game should
look like a place rather than a visible gameplay grid.

The reference is a design language, not a source of distributable assets.
ScummVM and Pentagram may be studied for compatible behavior, file-format
knowledge, projection, and object ordering. Their open-source licenses do not
grant a license to redistribute the original *Ultima VIII* art, maps, audio, or
other game data. Shipping assets must be original or separately licensed.

Private extractions from a legally owned installation may be used as reference
boards. World tiles are reference for projection, edge behavior, footpads,
height, occlusion, and material rhythm. Sprites are reference for scale,
readability, anchoring, directional frames, and animation cadence. They are not
paint-over bases for shipping art.

## Rendering rules

- Use a 2:1 oblique tile projection: 16×8 logical floor diamonds at the native
  resolution.
- Place actors by their feet. A normal human silhouette should be roughly
  46–52 pixels tall, with equipment visible on the body.
- Draw floor and architecture first, then emit characters, containers, corpses,
  and furnishings in diagonal `x + y` depth order.
- Use raised wall faces, pillars, furniture, rugs, lighting fixtures, and floor
  variation to break up the underlying tile structure.
- Keep interaction state in the simulation. The renderer projects that state
  and must not become the authority for collision, inventory, or combat.
- Avoid always-visible health bars and modern markers. Show them only as
  transient feedback or when state has changed.
- Preserve nearest-neighbor scaling and integer-aligned shapes at 640×400.
- Open the Windows build at 1280×800 by default: an exact 2× display scale.
- Target a normal human at approximately 50 pixels tall, or one eighth of the
  logical screen height. Wider action frames may extend well beyond the idle
  silhouette.

## Palette and interface

The world is deliberately more desaturated than *Ultima VIII*. Rooms favor
weathered timber, ash-grey stone, dried brick, tarnished metal, old bone, and
muted cloth. Hue and value variation should describe material without making
the floor visually compete with actors.

Characters may carry somewhat stronger local color. Monsters, loot, magic,
fire, poison, and interaction feedback own the highest saturation and contrast.
Black negative space frames the playable room. Interface panels use dark
leather-brown fields with narrow bronze edges and warm text.

The backpack, chest transfer, and combat feedback may use a fixed side panel
while the interaction model is being proven. Later presentation should favor
world-space containers and short, diegetic overlays where usability permits.

## Current vertical slice

`game/scripts/Main.cs` is the executable reference for:

- the floor projection and diagonal painter ordering;
- a raised two-sided room boundary;
- depth-sorted props, chests, creatures, corpses, and the player;
- an always-visible backpack on the player silhouette; and
- the earth-tone HUD and transfer panel.

Code-drawn art is temporary, but its scale, projection, layering, and palette
are constraints for replacement sprites.

Creature silhouettes and encounter art follow
[`bestiary-direction.md`](bestiary-direction.md).
