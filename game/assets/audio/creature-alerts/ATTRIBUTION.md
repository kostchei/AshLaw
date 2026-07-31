# Creature alert cues

The four alert cues are taken unmodified from
[80 CC0 creature SFX](https://opengameart.org/content/80-cc0-creature-sfx) by
**rubberduck**, released into the public domain under
[CC0 1.0](http://creativecommons.org/publicdomain/zero/1.0/). CC0 requires no
attribution; this file records the provenance anyway, because knowing where an
asset came from is worth more than the licence obliges.

| Cue | Source file | Length | Used for |
| :-- | :-- | --: | :-- |
| `alert-eep.ogg` | `cute_07.ogg` | 0.29 s | Vermin — the cave rat |
| `alert-snarl.ogg` | `grunt_02.ogg` | 0.53 s | Beasts and brutes — goblins |
| `alert-keen.ogg` | `weird_03.ogg` | 0.39 s | Aberrations — the many-eyed tyrant |
| `alert-hail.ogg` | `ooh.ogg` | 0.74 s | People |

## What still needs a human ear

These were chosen by name and length, not by listening to them. Audition all
four and swap any that do not read as the creature they belong to; the file
names are the contract, so replacing a file is the whole of the change.

`alert-hail.ogg` is a wordless human vocalisation standing in for the spoken
"hi" the design calls for. No CC0 pack surveyed carries that line, so a real
greeting needs a recording rather than a substitute from a creature pack.

## Format

Ogg Vorbis, loaded at runtime through `AudioStreamOggVorbis.LoadFromFile`
rather than `GD.Load`, so the cues need no `.import` sidecar and no editor pass
before the game can play them. An exported build must therefore include
`assets/audio/**` in its export filters, since files Godot never imported are
not otherwise packed.
