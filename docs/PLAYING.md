# Playing Mars

A short guide for the crew. Everything also works alone.

## Install

1. Run `MarsSetup.exe`, press **Install**. It finds Moon Sweepers by itself
   and backs up your saves first.
2. Once, it opens Nexus Mods for the patcher the game needs to load mods.
   Log in, press **Manual download** on the main file. The installer picks
   the download up from your Downloads folder by itself.
3. Press **Play**. The first start takes a minute or two longer.
4. In the main menu pick an empty slot: the game asks **Moon or Mars**.

Everyone in the crew needs the mod. Only the host's world decides what is on
Mars; the crew follows.

## The first hour

The box at the top right shows the current goal and the ground under you.
Each goal pays the crew a bonus. `goals` in the terminal lists them all.

1. **Drill.** Build tool, Production: the ore drill goes on a mine (the
   orange stake with the cyan light). It digs by itself; E takes the ore.
2. **Container port** (Logistics): E puts your Mars backpack into the crew
   storage. Building materials come from there.
3. **Smelter**: E takes plates and loads hematite from your backpack.
4. **Power.** Machines run on 20 % without power. A solar panel and a
   battery near the machines (25 m, light poles carry it further) make them
   run at full speed. Solar is weak in storms and gone at night.
5. **Logistics hub**: its drones empty every machine within 30 m into the
   crew storage and refill their inputs. From here the base runs itself.
6. **Second chain**: basalt mine, crusher, fiber spinner, assembler: machine
   parts. Later machines cost parts too.
7. **Ice** is near the poles. Electrolyzer and fuel refinery make rocket fuel
   for the generator, the aliens and the cannon.
8. **Aliens.** Three outposts glow purple. They trade alien alloy for what you
   make; the offers change every 8 minutes.
   Meteorites (announced, glowing orange) sometimes carry alloy too: be
   the first one there.
9. **Shield generator**: when a storm comes it raises a dome. Look at it to
   see the covered ground. It needs its full 30 kW, so give it a generator
   or a big battery.
10. **Planet cannon**: 10 alien alloy, 40 machine parts, 30 rocket fuel and
    60 kW. Shift + E fires it at the Moon.

## Storms

Watch the horizon: a wall of dust comes with a 75 second warning. Machines
standing outside fill with dust and jam; hold E to scrub them. Roofs, walls
and shield domes keep the dust out. Light poles clear the air around them so
you find your way home.

## Money

Cleaning pays like on the Moon. Far from the Collector the ground gets
richer (drift, crust, magnetite): up to four times the sector bounty, but it
scrubs up to twice as slow. Mars materials sell at the container port
(Shift + E, everything) or the market board (E, the best price; Shift + E
switches on auto-selling at good prices).

## Keys and commands

| | |
| --- | --- |
| E / Shift + E | use a Mars building (the prompt says what) |
| hold E | scrub a dusty machine |
| Ctrl + Shift + K | the Mars terminal; `help` lists everything |
| `goals`, `power`, `aliens`, `market`, `mines` | where things stand |
| `tp ice`, `tp alien`, `tp mine`, `tp base` | go there (testing) |
