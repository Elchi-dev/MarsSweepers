# Changelog

Mars Sweepers (the mod), MarsSetup (the installer) and MarsProbe (the research
plugin). Newest first.

## 0.9.9

### Fixed
- Windows settings attached to MoonSweepers.exe that make it load
  winhttp.dll and version.dll from System32 instead of the game folder
  (a compatibility layer, a Program Compatibility Assistant record, Image
  File Execution Options) are shown in the status, saved to a file and
  removed by [1] and [2]. The shim engine (apphelp.dll) was loaded into the
  game right before UnityPlayer.dll in the 2026-11-11 reports, and the game
  got Windows' copies of both DLLs while the game folder held Doorstop.
- The diagnosis reads the image load policy of the running game.

## 0.9.8

### Fixed
- Diagnosis: modules are read every second for 20 s, and the last list a
  process had is kept when it exits early (a start that ended within 3 s
  left nothing). The status no longer says winhttp.dll is missing when
  BepInEx is set to start through version.dll. The report includes the
  game's crash folders.

## 0.9.7

### Added
- Setup options [10] and [11]: start BepInEx through version.dll instead
  of winhttp.dll, and back. Doorstop carries the functions of both. Needed
  when something in the game process loads Windows' own winhttp.dll before
  the game asks for it, which is what a report showed on 2026-11-11.
- The diagnosis lists every module of the game process in load order and
  marks those that are neither the game's nor Windows'.

## 0.9.6

### Added
- Diagnosis: the game's loaded modules are read 3, 8 and 15 s after start
  (the first version waited 25 s, longer than the game ran), plus the
  Windows build and recent updates, whether winhttp.dll is a KnownDLL, and
  which proxy DLL names the game's executables use.

## 0.9.5

### Added
- Setup diagnosis (option 9, and on every game start the setup makes):
  which game processes ran, with their command line and parent; whether
  each one loaded BepInEx's winhttp.dll from the game folder and BepInEx's
  .NET runtime; every loader file compared byte for byte with BepInEx
  be.791; Smart App Control, Code Integrity blocks and Defender detections
  about the game folder. All of it goes into the report.

## 0.9.4

### Fixed
- Install now puts BepInEx's loader files (winhttp.dll, doorstop config,
  the .NET runtime, core) back every time, even when the right BepInEx is
  already there, so a changed or disabled loader is repaired.
- The setup checks what stops BepInEx from starting at all and says so:
  a missing winhttp.dll or runtime, `enabled = false`, other proxy DLLs of
  other loaders, and Steam launch options that switch it off. The report
  includes the launch options and the doorstop config.

## 0.9.3

### Added
- `mars slot <n>`: makes an empty save slot a Mars slot from the terminal,
  also in the main menu, in case the Moon or Mars question does not show.

### Fixed
- The setup report includes every BepInEx log file, not only the first
  one (a second game start writes to another file).

## 0.9.2

### Fixed
- Setup option [2] (start the game) started whatever Mars mod was already
  installed, which could be months old. It now installs this setup's mod
  first when the installed one differs, and the status line says when the
  installed mod is an older version.

## 0.9.1

### Added
- `collect`: writes everything the next features are built from in one go:
  the economy dump, a deep dump of the world's belts, belt queues, machine
  inputs and outputs, production, cargo and power grids, and a MarsProbe
  snapshot. It also runs by itself once per world, a minute after loading.
- `deep [fields]`: the deep dump alone, optionally with more world fields.
- MarsProbe's tracer also counts belt, machine, cargo and power methods.

## 0.9.0

### Added
- Storms jam machines: during a storm, dust builds up on drills and
  smelters that stand outside (a roof protects them). A dusty machine runs
  slower, a full one stops. Hold the interact key on it for 3.5 s to scrub
  it clean.
- Light pole ($60, Services): a lamp that turns on at night and in storms,
  and the storm air clears up within about 20 m of it, so a line of poles
  makes a path home. New Blender model.
- The Mars market: prices drift every 20 s and are pulled back towards
  normal, a boom or a crash hits one material every 13 minutes or so for a
  few minutes, and selling a lot lowers the price for a while. A ticker at
  the top of the screen (`MarketTicker` in the settings turns it off), the
  `market` command, and the container port sells at market prices.

### Verified
- Builds. Not run in the game yet.

## 0.8.1

### Added
- `economy` command: writes the whole upgrade tree (prices, requirements,
  effect per level), the money settings, section count and weight, building
  prices and the current multipliers to `BepInEx/MarsMod/research/economy.txt`.
  Works in Moon worlds too.
- Economy log: every 15 s of play, in any world, one row with money earned
  and spent, deposited kilograms, cleaned sections and upgrade levels, in
  `BepInEx/MarsMod/economy/`. It only reads. These numbers are what Mars
  prices and machine speeds get balanced against.

### Changed
- Licensed under MIT (was all rights reserved).

### Fixed
- The setup report now includes each Mars world's data, building and
  settings files (it looked for a file name no longer used).

## 0.8.0

### Added
- Mars machines in the game's own build tool: Ore drill and Smelter under
  Production, Container port under Logistics, each with a card picture. The
  game does the ghost, footprint checks, payment, removal, refunds and moving.
- Placement rules on top of the game's: the drill only on a free mine, every
  other building keeps 7 m away from mines. The ghost turns red and says why.
- Terminal: mouse wheel scrolling, Ctrl+Page Up and Ctrl+Page Down jump to the
  top and the end, 3000 lines of history, new output no longer moves the view
  while scrolled up.

### Changed
- Mars buildings never reach the game's save file. When a Mars world is
  saved they are written to `mars-structures.json` next to it and put back
  when the world loads, so the save stays readable without the mod.
- The N menu only opens when the game's build menu could not take the
  machines.

### Verified
- Builds. Not run in the game yet.

## 0.7.0

### Added
- Mines: a home mine 34 to 60 m from the base, 36 more placed from the
  world's seed, never on steep ground or near stations. Mines never run out.
- Ore drill, Smelter and Container port with production, a Mars backpack per
  player and crew storage that can be sold.
- Own building system (N menu, ghost, rotate, remove), run by the host and
  synced to the crew.
- Test commands: `money`, `unlock`, `give`, `freebuild`, `fastmachines`.

## 0.6.1

### Changed
- Ore drill model in the game's machine style: its material names, the
  chamfered plinth with cyan edge lights, the game's conveyor ports.

### Fixed
- The poster did not show: the wrong `LoadImage` overload was picked.

## 0.6.0

### Added
- Mars co-op channel over Steam networking messages (channel 7).
- Per-world Mars data, written atomically with a backup, sent from the host
  to the crew.
- Model pipeline: Blender sources in `models/`, a glTF loader that maps
  materials to the game's own.
- `research` command: building kinds, the build catalogue, prefabs,
  materials and reference renders.
- The "Keep Calm" poster in the station.

### Changed
- Storms come a little more often.

## 0.5.0 to 0.5.3

### Added
- New Mars air: a sky dome and fog shells, wind particles, a storm front,
  wind sound and an alarm. Storms stop at walls and roofs.
- Mars terrain profile 3: a home valley that keeps the spawn crater and a
  walkable rim.
- Settings per world (`MarsSessions/slot-N/marsmod-world.cfg`).
- The pause menu logo, the Mars contract, morning start, terminal editing.

### Fixed
- The map (M) is no longer hidden by the haze.
- Profile 2 put floor into the base and made the rim too steep.

## 0.4.0 and 0.4.1

### Added
- Dev terminal (Ctrl+Shift+K) with storm, haze, teleport, fly and settings
  commands, narrator lines, the Mars loading screen.

## 0.3.0 to 0.3.4

### Added
- Mars dust, Mars texts, intro and contract, haze, Phobos, dust storms.

### Fixed
- A startup crash: no injected components, the frame tick rides on game
  methods instead. Hooks with struct parameters are refused.

## 0.1.0

### Added
- The Moon or Mars choice for a new save slot, Mars save folders, MarsProbe
  and the first installer.
