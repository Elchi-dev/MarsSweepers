# Hooks

Every game method Mars Sweepers patches, for Moon Sweepers 1.0.5 (Unity
6000.6.0f1, IL2CPP). Written for anyone building a loader adapter or
checking a new game version. All of them are installed in
`MarsMod/Patches.cs` with HarmonyX through BepInEx 6 be.791.

## Rules

- **No method that takes or returns a game struct by value.** On this Unity
  version a Harmony trampoline over such a method corrupts the runtime.
  `Patches.Patch` refuses them and logs `skipped, struct parameter`.
- **No injected MonoBehaviours.** Class injection crashed the game at
  startup in 0.3.0. The per-frame tick rides on two game `Update` methods
  instead.
- A hook never throws into the game: every body is wrapped and logs once.

## The list

| # | Method | Kind | What for |
| --- | --- | --- | --- |
| 1 | `MoonSaveSlots.get_Folder` | post | Mars worlds save under `MarsSessions/` instead of `MoonSessions/` |
| 2 | `MoonSaveSlots.SlotFolder(int)` | post | same, per slot |
| 3 | `MoonSaveSlots.BackupFolder(int)` | post | Mars backups next to Mars saves |
| 4 | `MoonSaveSlots.Root(string)` | post | same, for the current world |
| 5 | `MoonSaveSlots.Backups(string)` | post | same |
| 6 | `MoonSaveSlots.Delete(int)` | pre + post | deleting a Mars slot also forgets it is Mars |
| 7 | `MoonMainMenu.SlotLine(int)` | post | "MARS" in front of Mars slots |
| 8 | `MoonMainMenu.Play` | pre | Moon or Mars question for an empty slot |
| 9 | `MoonMainMenu.PlayChosen` | pre | same |
| 10 | `MoonSession.StartShift` | pre | fallback: switch to Mars where the host's world starts |
| 11 | `MoonSession.ReloadShift` | pre | same, on reload |
| 12 | `MoonSession.JoinCrewShift` | pre | crew joins a Mars lobby: Mars settings before the world exists |
| 13 | `MoonWorld.OnEnable` | pre | Mars planet settings before the terrain is built |
| 14 | `MoonWorld.Initialize` | pre | same |
| 15 | `MoonWorld.Start` | pre | same |
| 16 | `MoonWorld.OnDestroy` | post | leaving a world |
| 17 | `MoonNewHire.LoadText(string)` | post | the Mars contract replaces the raw contract text |
| 18 | `MoonWorld.BuildDefinition(int)` | post | kinds 193 and up return the Mars machine definitions |
| 19 | `MoonDepotBuilder.CardArt(string)` | post | card pictures for `marsmod.*` images |
| 20 | `MoonDepotBuilder.CanBuild(int)` | post | Mars machines are always buildable on Mars |
| 21 | `MoonDepotBuilder.ProductionNote(int)` | post | the card's description text |
| 22 | `MoonDepotBuilder.get_PlacementValid` | post | Mars placement rules (drill on a mine) turn the ghost red |
| 23 | `MoonDepotBuilder.get_PlacementReason` | post | and say why |
| 24 | `MoonDepotSave.Write(BinaryWriter, MoonLooseDustSnapshot)` | pre + post | takes Mars buildings out of the save, writes `mars-structures.json`, puts them back |
| 25 | `MoonWorld.RestoreDepots(MoonLooseDustSnapshot)` | pre | adds Mars buildings back before the world builds its structures |
| 26 | `MoonCrashHeartbeat.Update` | post | the mod's per-frame tick |
| 27 | `MoonFramePacer.Update` | post | same, whichever runs (deduplicated per frame) |
| 28 to 35 | `MoonMainMenu.StartGame`, `ClickSlot`, `ReadyOrStart`, `MoonSteamLobby.Create`, `StartShift`, `EnterShift`, `MoonSession.Load`, `ReturnToMenu` | post | diagnostics only: log the start flow |

| 36 | `MoonWorld.PaySection(int, string)` | pre + post | dust tiers: reads the bounty the game paid, adds the tier bonus (host, Mars only) |
| 37 | `MoonWorld.EnterMachine(MoonDepotSnapshot, int, int)` | post | belt research: logs the first 40 canisters entering machines |
| 38 | `MoonWorld.EmitCargo(MoonConveyorSnapshot, string, MoonCargo, double, out string)` | post | belt research: logs the first 40 cargo emits |

| 39 | `MoonWorld.CleaningDepthMultiplier(string)` | post | dust tiers: the local player scrubs slower in crust and magnetite sectors |

| 40 | `MoonWorld.RouteEntranceClear(int, int, int)` | post | logic gates (experimental; only installed once a gate exists): outputs that do not fit the rule report blocked |

`EnterMachine` (37) also has a prefix since 0.10.0: with `beltmode on` Mars
machines take Mars canisters, and the logic gates learn which canister a
splitter is handling.

Hooks 36 to 40 are new in 0.10.0 and have not run in the game yet.

## Not hooked, called

The mod calls these directly (no patch): `MoonWorld.SetTeamDollars`,
`DevAddMoney`, `WalletDollars`, `WeighHex`, `Regions.Centers`,
`TryGetCanister`, `MachineInputAt`, `BeltStartingAt`, `BeltEndingAt`,
`MoonEnding.Play(true)` (the ending, in preview mode), `ChargeBuild`, `TryPurchaseTreeNode`,
`MoonDepotBuilder.BeginPlacementFromShop`, `PlacementPose`,
`RefreshCatalog`, `MoonCrewSession.SendBytes`, and Steam's
`SteamNetworkingMessages.ReceiveMessagesOnChannel` on channel 7.

## Memory the mod changes

- `MoonDepotBuilder.baseStructures` (static): Mars cards appended while a Mars
  world is open, the original array put back when it closes.
- `MoonWorld.depotDefinition`: a copy whose `additionalStructures` is 192 plus
  the Mars kinds long. The game's own definition object is not touched.
- `MoonWorld.landmarkKindCount`, `raisedKindCount`, `landmarkPlaced`: grown to
  fit the Mars kinds when they are shorter.

No machine code is patched. `MoonDepotSave.Read` still refuses kinds above
192, which is fine because the save file never contains them.
