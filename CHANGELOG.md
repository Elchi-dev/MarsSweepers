# Changelog

Mars Sweepers (the mod), MarsSetup (the installer) and MarsProbe (the research
plugin). Newest first.

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
