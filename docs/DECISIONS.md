# Decisions

## Mars buildings are game buildings with their own kinds (0.8.0)

The game numbers its buildings 1 to 192 (`MoonDepotSave.MaxStructureKind`).
Mars machines take 193 and up, with a definition copied from the safety
barrier (kind 9, a plain prefab without scripts) and our model as the
prefab. That way the hammer, the ghost, the footprint check, payment,
removal, refunds, moving and the crew sync are the game's own.

Rejected: our own build system (0.7.0). It worked, but it felt detached from
the game: its own ghost, its own keys, no card in the menu.

Rejected: patching the save reader's `cmp eax, 192` so the game save can hold
kinds above 192. It works, but then a Mars save opened without the mod has
buildings the game rejects, and the patch is a byte at a fixed address in
one game build.

## The game save never holds a Mars kind (0.8.0)

`MoonDepotSave.Write` is patched to take Mars buildings out of the snapshot
before it is written and to put them into `mars-structures.json` next to the
save, by string id (`marsmod.building.drill`). `MoonWorld.RestoreDepots`
adds them back before the world builds its structures. Kind numbers only
exist while the game runs, so another mod can take 193 tomorrow and our
saves still load, as long as the string ids match.

## String ids: `modid.category.itemid`

Everything another mod could collide with is named by a string: buildings,
items, data sections. Numbers are handed out at runtime. This is meant to
become the shared standard for Moon Sweepers mods.

## No struct hooks, no injected components (0.3.2)

Both crashed the game (`HOOKS.md`). The rule is enforced in code.

## Mars data per world, host owns it (0.6.0)

`MarsSessions/slot-N/mars-data.json`, one JSON section per feature, written
atomically with a `.bak`. The host decides everything and sends changes to
the crew on its own Steam channel (7). Mines need no traffic: every client
places them from the world's seed and the terrain.

## Models are made in Blender, never in code (0.6.0)

`models/*.py` builds each model in Blender (4.2) and exports `.glb` with
markers (`port_N`, `anchor_*`, `anim_spin_*`, `COL_*`). The loader maps the
material names to the game's own materials, so Mars machines are lit and
shaded like the game's.

## Licence: MIT

The mod is meant to be read and copied: it is the first project built the
way the shared Moon Sweepers modding standard is meant to work, and its
hooks, save side file and building registry are examples for other mods.
MIT asks nothing of them but the notice. Third-party parts keep their own
terms (NOTICE): the poster stays under the Unknown Worlds fan content
policy (non-commercial), the narrator voice under Chatterbox's MIT.
