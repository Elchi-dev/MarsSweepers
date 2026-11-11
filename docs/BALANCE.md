# Balance

How Mars prices and rates were set. All numbers live in `MarsMod/MarsItems.cs`;
change them there. Measured Moon numbers come from the economy dump
(`economy` in the terminal) of a mid-game Moon world, 2026-11-11.

## The Moon, measured

| What | Value |
| --- | --- |
| Dust | $1 per kg, sand x3, stone x7 |
| Section bounty | $10 per cleaned section (one section holds about 30 kg of dust) |
| Mid-game crew income | about $100 a minute (wallet about $5k, 8 t deposited, 175 buildings, payout x1.06) |
| Upgrade tree | payout $120 / $400 / $1100, autopack $2000, exchange $2500 |
| Vanilla buildings | solar $250, battery $160, packager $100, floodlight $85, conveyor $40, splitter and merger $5 |

So a mid-game crew pays for a $500 machine in five minutes, and the dearest
tree nodes cost 20 to 25 minutes of income.

## Mars materials (normal market price)

| Item | $ | Made from | Value added |
| --- | --- | --- | --- |
| Hematite ore | 1.5 | drill on a hematite mine, 1 every 4 s | 22.5 $/min per drill |
| Basalt | 0.8 | drill on a basalt mine | 12 $/min per drill |
| Water ice | 1.2 | drill on an ice mine (near the poles) | 18 $/min per drill |
| Iron plate | 4.5 | 2 hematite, 6 s | 15 $/min per smelter |
| Basalt grit | 1.0 | 1 basalt makes 2, 4 s | 18 $/min per crusher |
| Basalt fiber | 5 | 3 grit, 6 s | 20 $/min per spinner |
| Machine part | 22 | 2 plates + 1 fiber, 10 s | 48 $/min per assembler |
| Oxygen, hydrogen | 3 each | 1 ice makes one of each, 8 s | 36 $/min per electrolyzer |
| Rocket fuel | 12 | 1 hydrogen + 1 oxygen, 8 s | 45 $/min per refinery |
| Alien alloy | 90 | only from alien traders | |

One drill and one smelter (about 37 $/min together, on an $750 build) sit at
a third of the Moon's mid-game income, so Mars starts slower than the Moon
ends. A full machine-part line (3 drills, 2 smelters, a crusher, a spinner,
an assembler) makes about 150 $/min and costs about $3600 plus power, which
is the point where Mars overtakes the Moon.

## Power

| Building | Makes or holds | Uses |
| --- | --- | --- |
| Solar panel ($150) | 8 kW in full sun, 2 kW in a storm, 0 at night | |
| Battery ($200) | 120 kW·min | |
| Fuel generator ($600 + 4 parts) | 30 kW, 1 rocket fuel a minute (12 $/min) | |
| Drill / crusher / spinner | | 3 / 4 / 3 kW |
| Logistics hub ($900 + 20 plates) | | 4 kW |
| Smelter / assembler / refinery | | 6 kW each |
| Electrolyzer | | 12 kW |
| Shield ($2500 + 20 parts + 10 fiber) | | 30 kW, only while a storm is near |
| Planet cannon | | 60 kW once loaded |

A day-only base needs about one panel per drill; a battery carries one
drill and one smelter for about 13 minutes of night. The shield is meant to
need a generator or a big battery bank: storms are exactly when solar fails.

## Sector bounty tiers

Fine dust x1, dust drift x1.6, dust crust x2.5, magnetite crust x4 of the
game's own bounty. Scrubbing goes 1, 0.8, 0.65 and 0.5 as deep per pass, so
the best tier takes twice as long and still pays twice as much per minute. The tier comes from smooth noise plus the distance from
the Collector, so the far side of Mars pays several times more.

## Alien trades

Every alloy offer asks for about $300 or more of materials at normal
prices (25 rocket fuel, 60 fiber, 70 iron plates, 100 oxygen, 20 machine
parts) or $3000 in cash, far above alloy's $90 sale price: alloy is for
building, not selling. The cannon needs 10, about an hour of a running
machine-part line. Big orders (40 parts and 15 fuel, or 120 fiber and 80
plates, for 4 alloy) come to about $240 to $265 per alloy. The traders also
buy alloy back, for 8 machine parts ($176) or $200, below the cheapest way
to get one, so no trade loop makes money by itself.
