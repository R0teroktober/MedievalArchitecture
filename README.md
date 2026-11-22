# Medieval Architecture

A [Vintage Story](https://www.vintagestory.at/) mod that adds medieval building blocks: archways, tiny windows, windowsills, leaded glass windows, gates, portcullises, drawbridges, big trapdoors and braziers.

Most stone constructions are not crafted in one go, but built step by step in the world – the material you use in each step decides how the result looks. Rim stones and fill can be chosen freely, so a granite rim filled with basalt cobblestone or plaster is possible. Materials of other mods are supported as well.

- **Game version:** 1.22 (branch `1.22`). Version 1.21 is on branch `master`.
- **Required:** [Attribute Rendering Library](https://mods.vintagestory.at/attributerenderinglibrary) 3.0.1 or newer.
- **Optional:** [Config lib](https://mods.vintagestory.at/configlib) (settings in game), Real Smoke (brazier smoke).

## Content

### Archways

Small, medium (2×1, 2×2) and big (3×2, 3×3) archways in several styles.

1. Craft a **construction frame** (plumb and square plus planks, logs, support beams and nails, depending on the size) and place it where the archway should be.
2. Hold the next material and press <kbd>Ctrl</kbd> + <kbd>right mouse button</kbd> on the frame:
   1. loose **stones** – they become the rim of the archway,
   2. **mortar**,
   3. a **fill block**: rock, cobblestone, stone bricks, polished rock, dry stone, plaster, wattle and daub, fired bricks, clinker bricks, brick courses, uneven bricks, rammed earth or mud bricks.
3. The finished archway replaces the frame and the empty frame drops.

The interaction help shows what to add next as soon as you hold a matching item. Afterwards you can

- insert a **glass pane** or a **leaded glass window** to get an archway with a window (all sizes),
- add **boards** to get a door lintel for a vanilla door (small and medium archways),
- place a **gate** or **portcullis** behind a 3 block wide archway.

### Tiny windows and windowsills

Built in two steps: the recipe decides the rim stones, the fill block is added in the world.

- **Tiny windows** (5 shapes): stones, mortar and a chisel. Place it, then <kbd>Ctrl</kbd> + <kbd>right mouse button</kbd> with a fill block. A glass pane can be added afterwards.
- **Windowsill:** hammer, chisel, plumb and square and 2 stones. Place the windowsill frame and finish it with a fill block.

Hammer, chisel and plumb and square stay in the crafting grid.

### Leaded glass windows

Single, top, middle and bottom panes made of lead bits, a glass slab and planks. They can be placed on their own or inserted into archways.

### Gates, portcullises and drawbridges

- **Gates:** 3 and 4 blocks tall, made of planks and nails.
- **Portcullises:** 3 and 4 blocks tall, made of a wooden axle, lead ingots, wooden fences and rope.
- **Drawbridges:** small, medium and big, made of helve hammers, lead plates, plank slabs and floor chains.

They behave like one big vanilla door: right click on any part opens or closes them. Reinforcing and locking works on every part while sneaking (plumb and square, padlock); reinforcement is removed at the bottom corner where the gate was placed.

The small gate seals rooms (e.g. for the cellar bonus). The big gate has a peephole and does not. Gates and raised drawbridges hold back water, portcullises do not.

### Trapdoors

Small and big trapdoors, flat or sloped, single or double, made of a vanilla trapdoor or door, planks and nails. Trapdoors seal rooms and hold back water like vanilla trapdoors.

### Brazier

Smith 4 **brazier parts** from metal ingots and combine them with a **metal hoop**. Place the empty brazier, add 3 pieces of firewood and light it. A brazier burns much longer than a firepit and can be put out with water. The decorative vanilla brazier clutter can be turned into a working brazier with a fired bowl and some acid.

## Supported mods

Rim stones and fill materials of these mods can be used without any further setup:

- **Geology Additions** – all rocks as rim stones and fills
- **Material Needs** – plaster, rammed earth, mud bricks and daub colors
- **Material Needs: Geology** – all rocks as rim stones and fills
- **Wildcraft: Trees and Shrubs** – all woods for door lintels
- **Thievery** – gates, portcullises, drawbridges and big trapdoors can be locked and unlocked with Thievery locks, like vanilla doors. Like vanilla multiblock doors, this only works at the root block (the bottom corner where the gate was placed).

Other mods can add their materials with a JSON patch, see [docs/MODDING.md](docs/MODDING.md).

## Configuration

`ModConfig/medievalarchitecture.json`:

| Setting | Default | Meaning |
|---|---|---|
| `RimStoneAmount` | 6 | Stones needed for the rim of an archway frame (bigger frames need a multiple). |
| `BrazierBurnDurationModifier` | 30 | How much longer firewood burns in a brazier than in a firepit. |
| `GlassCodeByType` | vanilla glass colors | Maps glass items to the glass of an archway window. |

With Config lib installed, whole groups of blocks can be switched off in game: archways and tiny windows, windows, windowsills, trapdoors, braziers, gates and drawbridges.

## For modders

How to add materials, use the rim stone registry for your own blocks or define gates: [docs/Modding.md](docs/Modding.md).

## Credits

Roteroktober, 12Port, Craluminum2413 (Dana).
