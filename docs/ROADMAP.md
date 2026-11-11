# Roadmap

Mars Sweepers is meant to feel like a second, bigger game on top of Moon
Sweepers: Mars is larger, dirtier and needs real industry.

## Next

1. **Test 0.10 in the game**: power, the shield dome, the new machines and
   their models, ice mines, the alien outposts, the ending, the setup
   window on a clean PC.
2. **Belts.** The game's conveyors carry Mars items. Any side of a machine
   can be input or output, the belt's direction decides. Plan and what is
   measured first: [BELTS.md](BELTS.md).
3. **Container port fed by belts.** Accepts only Mars materials and stores
   them for the crew (building, crafting, selling).
4. **Logic gate.** A belt splitter you set up by looking at it: left or right
   depending on the item, a stored amount (counting items already on their
   way, with a gap so it does not flip every item) or a market price.
5. **Balance** after the first play sessions (docs/BALANCE.md has the model).

Done in 0.9.0: the market, storm jams with scrubbing by hand, light poles.
Done in 0.10.0: power, shields, the second chain, ice and rocket fuel, the
market board, item costs, dust tiers, alien traders, the ending, the setup
window.

## Later

- Thicker dust in higher tiers (today the tier is only the reward).
- Storms jam belts too; scrubbing gets faster with upgrades.
- Gates that read prices from the market board.
- More alien offers, and outposts that move after a while.

## The shared standard

Mars Sweepers is built standalone first. Its reusable parts (building
registry, co-op channel, per-world data, item registry, model import) are
kept separate from the Mars content so they can move into a shared modding
API for Moon Sweepers, with string ids (`modid.category.itemid`).
