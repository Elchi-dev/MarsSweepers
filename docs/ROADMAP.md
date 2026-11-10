# Roadmap

Mars Sweepers is meant to feel like a second, bigger game on top of Moon
Sweepers: Mars is larger, dirtier and needs real industry.

## Next

1. **Test 0.8 in the game**, alone and with a second player: cards, the
   ghost, saving and loading, the crew sync of kinds above 192.
2. **Balance.** Read the upgrade tree (prices and effects) and how section
   money grows, model what a team earns per minute at each stage, and place
   Mars after the Moon on that curve. Base values: $1 per kg, sand 3x, stone
   7x, $10 per cleaned section; machines cost $40 to $1000.
3. **Belts.** The game's conveyors carry Mars items. Any side of a machine
   can be input or output, the belt's direction decides.
4. **Container port fed by belts.** Accepts only Mars materials and stores
   them for the crew (building, crafting, selling).
5. **Logic gate.** A belt splitter you set up by looking at it: left or right
   depending on the item, a stored amount (counting items already on their
   way, with a gap so it does not flip every item) or a market price.
6. **Market.** Prices that move (drift, booms, crashes, selling a lot lowers
   your own price), shown on a board and in the HUD, readable by gates.

## Later

- Dust tiers per sector: dearer dust takes longer to scrub.
- Storms jam belts and machines; cleaning them by hand takes a few seconds
  (faster with upgrades).
- Power: solar is weak in storms, batteries, a storm-proof generator.
- Light poles: an early safe zone in a storm.
- Shield generators: a lot of power, only on when a storm is near, the
  covered area found by flood fill so nothing gets through; the base too.
- Advanced machines and shields cost Mars products, not only money.
- The ending: a clean Mars reflects the sun onto the cleaned Moon, using the
  game's own ending.
- Alien traders (design open).

## The shared standard

Mars Sweepers is built standalone first. Its reusable parts (building
registry, co-op channel, per-world data, item registry, model import) are
kept separate from the Mars content so they can move into a shared modding
API for Moon Sweepers, with string ids (`modid.category.itemid`).
