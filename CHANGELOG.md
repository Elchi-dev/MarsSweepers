# Changelog

Mars Sweepers (the mod), MarsSetup (the installer) and MarsProbe (the research
plugin). Newest first.

## 0.10.0

### Added
- Power grid. Buildings that make, store or use power within 25 m of each
  other form a grid (light poles carry it on). Mars solar panel (8 kW in
  full sun, a quarter in a storm, nothing at night, less with dust on it),
  battery (120 kW·min) and fuel generator (burns 1 rocket fuel a minute for
  30 kW when the grid runs short). Machines on a weak grid slow down; with
  no power they crawl on backup cells at 20 %. `power` lists every grid.
- Shield generator. When a storm comes and it has its full 30 kW, it raises
  a dome. What it covers is a flood fill over the ground (2 m cells, up to
  28 m, stopped by walls, cliffs and pits). Under the dome the air is clear,
  dust does not settle on machines and solar panels get full sun.
- Second production chain: crusher (basalt to grit), fiber spinner (grit to
  basalt fiber), assembler (iron plates and fiber to machine parts).
- Water ice mines near the poles, electrolyzer (ice to oxygen and hydrogen)
  and fuel refinery (hydrogen and oxygen to rocket fuel).
- Market board: every Mars price and where it is heading; sells from the
  crew storage, the best-paying material first or everything at once.
- Item costs: the later machines also take materials from the crew storage
  when built (the build tool says what is missing).
- Dust tiers. Sectors hold fine dust, drifts, crust or magnetite crust; the
  tier pays x1, x1.6, x2.5 or x4 of the normal sector bounty and takes up
  to twice as long to scrub (each pass goes 1, 0.8, 0.65 or 0.5 as deep).
  Tiers climb with the distance from the Collector. A small box at the top
  right shows the ground under you and the current goal.
- Alien trader outposts: three per world. They trade alien alloy for what
  the crew makes; two offers each, new ones every 8 minutes.
- The ending: the planet cannon (alien alloy, machine parts, 30 rocket fuel,
  60 kW) fires at the Moon. The game's own ending sequence plays with the
  Moon in the sky. `ending` plays it right away (host, cheat).
- Blender models and build cards for all new buildings, the ice mine and
  the outpost.
- Mars contract goals: eight steps from the first drill to the planet
  cannon, one at a time, each with a cash bonus for the crew ($150 to
  $2000). `goals` lists them; finishing one tells everyone.
- Belt research: passive hooks log the first canisters entering machines
  and cargo put on belts, and `belts` shows what the game says about the
  ports of the nearest Mars building. The plan is in docs/BELTS.md.
- MarsSetup is a window now: Install / Play, the patcher download is picked
  up by itself, no terminal. It installs for playing: no MarsProbe and no
  BepInEx console window. `MarsSetup.exe --dev` opens the old tester menu.

### Changed
- Every Mars building is driven by its catalogue entry (MarsItems.cs):
  one place for prices, recipes, power and item costs.
- Removing all modding also puts the game's own dstorage.dll back.

## 0.9.16

### Fixed
- `money` and every payout (selling, refunds of N-menu buildings) did
  nothing: MoonWorld.DevAddMoney has no effect in the release game. The team
  balance is now set with SetTeamDollars and read back; a mismatch is
  logged once.
- Every install sets up the dstorage.dll route by itself. Confirmed on the
  affected PC (2026-11-11 22:50): BepInEx starts through it.

## 0.9.15

### Added
- Setup option [15], undone by [16]: starts BepInEx through dstorage.dll.
  Switching PreferSystem32 off for the exe did not change the running game
  (2026-11-11 22:37 report; others see the same since a Windows 11 update).
  dstorage.dll has no copy in System32, so Windows takes it from the game
  folder even with that rule. `native/dstorage-proxy` forwards every
  DirectStorage function to the game's own file (dstorage_original.dll)
  and loads Doorstop from BepInEx/doorstop by its full path.

## 0.9.14

### Fixed
- Setup option [14]: Windows started MoonSweepers.exe with the exploit
  protection rule "prefer system images" (PreferSystem32), read from the
  running game on 2026-11-11 22:34, also when the setup started the game
  itself. With it, Windows' own winhttp.dll beats BepInEx's loader in the
  game folder. [14] records where the rule comes from (Get-ProcessMitigation,
  Image File Execution Options) and switches it off for the game only.

## 0.9.13

### Added
- Diagnosis reads each game process's DLL search path, image path and
  current folder as Windows was given them at creation. The Process
  Monitor recording of 2026-11-11 22:28 showed the game looking for
  UnityPlayer.dll in System32, System and Windows before its own folder,
  so a winhttp.dll or version.dll in the game folder can never win.

## 0.9.12

### Added
- Setup option [13]: records the game's start with Microsoft's Process
  Monitor and keeps every lookup of winhttp.dll and version.dll with
  Windows' answer, in MarsProbe/loader-trace.csv and the report.

## 0.9.11

### Added
- Setup option [12]: start MoonSweepers.exe directly instead of through
  Steam (with steam_appid.txt so the game does not hand the start back),
  to tell whether the way Steam starts the game keeps BepInEx out.
- Diagnosis: Steam's version, file dates and update log; the image load
  policy of each game process as soon as it is seen.

### Changed
- Compatibility settings were not the cause on the affected PC: the
  2026-11-11 22:11 report found none.

## 0.9.10

### Fixed
- MarsProbe's tracer is off by default (new setting `Tracer.On`). The last
  BepInEx start on the affected PC (2026-11-10 04:13:30) ended 4 s in,
  right after the tracer had patched 200 game methods; every start after it
  ran with Windows' shim engine loaded and Windows' own winhttp.dll and
  version.dll, so BepInEx never started again.

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
