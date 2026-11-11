# Belts (plan)

Mars items should ride on the game's own conveyors. This is what is known
about the game's belts and what is still measured before any of it changes.

## What the game has (Moon Sweepers 1.0.5)

- Belts are `MoonConveyorSnapshot` (id, owner, start, end and their
  directions) in `MoonWorld.conveyors`. Each belt has a queue in
  `beltQueues`: (position along the belt, canister id).
- Everything on a belt is a canister (`MoonDroppedCanister`, a struct): id,
  owner, kilograms, capacity, pose and `cargo` (a `MoonCargo`: dust, sand,
  ...). Canisters live in `MoonWorld.canisters`; `nextCanisterId` counts up.
- A canister that reaches a machine input calls
  `EnterMachine(MoonDepotSnapshot machine, int input, int canister)`; the
  bool says whether the machine took it.
- Machines put cargo on a belt with
  `EmitCargo(MoonConveyorSnapshot outfeed, string owner, MoonCargo, double kg, out string reason)`.
- `MachineInputAt(point)`, `BeltStartingAt(point)` and `BeltEndingAt(point)`
  find what touches a port.

## The plan

1. **Measure (0.10.0).** Passive hooks on `EnterMachine` and `EmitCargo`
   log the first 40 calls; `belts` in the terminal shows the belts near the
   player, what the game says about the ports of the nearest Mars building
   and the conveyor ports in the game's definitions (vanilla machines and
   every Mars kind), and writes it all to research/belts.txt. Each
   definition carries `conveyorPorts` (name, position, direction, in or
   out); the Mars kinds are copies of a donor, so they carry the donor's
   ports, not their own. Open questions: does the game see the ports of a Mars
   building at all (they are built on a donor definition), and does
   `EmitCargo` return the new canister's id through `nextCanisterId`.
Steps 2 to 4 are built in 0.10.0 as an experiment, off by default:
`beltmode on` (host) switches them on. Whether they work depends on the
answers from step 1.

2. **Out.** A Mars machine whose output port starts a belt emits its product
   with `EmitCargo` as a tiny canister (so a vanilla packager pays next to
   nothing for it). The host keeps a map canister id to Mars item, saved
   with the Mars data and synced to the crew; the canister gets a tint per
   item.
3. **In.** A prefix on `EnterMachine` for Mars kinds: a canister in the map
   goes into the machine's inputs if they have room (the belt waits
   otherwise), unknown cargo is refused. Any side can be input or output:
   a belt that starts at a port is an output, one that ends there is an
   input.
4. **Container port** takes every Mars canister into the crew storage.
5. **Logic gate**, a Mars building on a belt junction: by item, by a stored
   amount with hysteresis (counting canisters already on their way), or by
   market price.
