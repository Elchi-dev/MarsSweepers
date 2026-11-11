# Architecture

Three programs live here:

| Part | What it is |
| --- | --- |
| `MarsMod/` | The mod, a BepInEx 6 IL2CPP plugin (`MarsMod.dll`) |
| `MarsSetup/` | The installer, one Windows `.exe` (.NET Framework 4.8) that carries BepInEx, the mod, MarsProbe and the narrator lines |
| `MarsProbe/` | A research plugin: dumps the game's types, scenes and definitions, traces calls, writes a report the installer zips up |

`models/` holds the Blender scripts the machine models are made from;
`assets/` holds what they produce (`.glb`, card pictures) plus the poster
and the narrator lines.

## How the mod runs

```
BepInEx loads MarsMod.dll
        |
        v
Plugin.Load: settings, save folders, patches (docs/HOOKS.md)
        |
        v
MoonCrashHeartbeat.Update / MoonFramePacer.Update  (postfix, once per frame)
        |
        v
MarsWorld.Frame
   |-- menu: Moon or Mars question, logo, texts
   |-- loading: red loading planet
   '-- in a Mars world:
         look and air       Look, Sky, Haze, Wind, Shelter, Dust
         network            MarsNet (Steam channel 7), MarsData (per-world JSON)
         gameplay           Mines, Buildings, VanillaBuild, BuildMode, Market, LightPoles
         tools              Terminal, Commands, Research
```

A Moon world never runs the Mars parts: every feature checks
`MarsState.CurrentIsMars` or `MarsWorld.InMarsWorld` first.

## Files of the mod

| File | Does |
| --- | --- |
| `Plugin.cs` | Entry point, startup order |
| `Patches.cs` | Every hook, and the refusal of struct hooks |
| `Guard.cs` | Remembers a crashed start and loads a safe mode next time |
| `MarsState.cs` | Which slots are Mars, save paths, the storm seed |
| `MarsProfile.cs` | Settings: global (this PC) and per world (`marsmod-world.cfg`), terrain profiles |
| `SettingsSwap.cs` | Puts Mars values into the game's `MoonSettings` before a world is built |
| `MarsWorld.cs` | The frame loop, entering and leaving a Mars world, notices |
| `Look.cs`, `Sky.cs`, `Dust.cs` | Colours of ground, dust and sky |
| `Haze.cs`, `Wind.cs`, `Shelter.cs` | Air, fog shells, storms and their schedule, wind and sound, what counts as indoors |
| `Text.cs`, `Contract.cs`, `Logo.cs`, `Poster.cs`, `Narrator.cs` | Mars texts, the contract, the pause menu logo, the poster, spoken lines |
| `Lobby.cs` | Steam lobby data (Mars flag, profile, version, seed), who the host is |
| `MarsNet.cs` | The co-op channel: ping, hello, snapshots, sections, requests, notices |
| `MarsData.cs` | Per-world Mars data: sections, atomic save, host to crew sync |
| `Gltf.cs`, `Models.cs` | glTF reader, game objects from models, materials, colliders, moving parts |
| `Mines.cs` | Home mine and seeded mines |
| `MarsItems.cs` | Items and the building catalogue |
| `Buildings.cs` | Machine state, production, backpacks, crew storage, requests to the host |
| `VanillaBuild.cs` | Mars machines as game building kinds, cards, placement rules, the save side file |
| `BuildMode.cs` | Using machines (E, hold to scrub), the HUD, and the fallback N menu |
| `Market.cs` | Moving prices, booms and crashes, selling pressure, the ticker |
| `LightPoles.cs` | Pole lamps and the clear air around them |
| `Economy.cs` | The economy log and the `economy` dump, for balancing |
| `Terminal.cs`, `Commands.cs` | The dev terminal (Ctrl+Shift+K) and its commands |
| `Research.cs` | The `research` command and PNG helpers |
| `MarsUi.cs`, `Structs.cs`, `Reflect.cs` | Small helpers |

## Files on the player's disk

```
<game>/BepInEx/plugins/MarsMod/MarsMod.dll
<game>/BepInEx/MarsMod/voice/*.wav           narrator lines (own files are never overwritten)
<game>/BepInEx/config/marsmod.mars.cfg       settings for this PC
<save>/MarsSessions/slot-N/                  a Mars world: the game's save files, plus
    marsmod-world.cfg                        this world's look and storm seed
    mars-data.json (.bak)                    Mars data (machines, storage)
    mars-structures.json (.bak)              Mars buildings, kept out of the game save
```

## Co-op

The host runs everything. A crew member sends a request (build, use, take)
to the host on channel 7; the host applies it and sends the changed data
section to everyone. Buildings placed with the game's tool go through the
game's own crew sync; the host notices them on its next check (once a
second) and makes their records.
