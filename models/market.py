"""Mars market board: a big screen on two legs that shows every Mars price and where it is heading.
Footprint 2.4 x 1.0 m, 3.3 m tall."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
base(2.4, 1.0, 0.14, 0.25)
for sx in (-1, 1):
    box(f"leg {sx}", (0.16, 0.2, 2.1), (sx * 0.95, 0.05, 1.15), GR, bevel=0.04)
    box(f"leg band {sx}", (0.2, 0.24, 0.08), (sx * 0.95, 0.05, 0.5), OR, bevel=0.02)
rounded_box("board", (2.3, 0.2, 1.4), (0, 0.05, 2.35), IV, radius=0.1)
box("board frame", (2.12, 0.05, 1.22), (0, -0.07, 2.35), GR, bevel=0.02)
box("screen", (2.0, 0.03, 1.1), (0, -0.1, 2.35), "mars:screen", bevel=0.01)
# price rows are drawn in game as live text on anchor_screen; the card render gets painted ones
import os as _os
if not _os.environ.get("MARS_NO_PREVIEW"):
    for r in range(5):
        z = 2.75 - r * 0.2
        box(f"row label {r}", (0.5, 0.012, 0.06), (-0.62, -0.118, z), IV, bevel=0.003)
        box(f"row bar {r}", (0.25 + 0.12 * ((r * 3) % 5), 0.012, 0.06), (0.15, -0.118, z), CY, bevel=0.003)
        box(f"row arrow {r}", (0.08, 0.012, 0.08), (0.8, -0.118, z), OR if r % 2 else "MC - Cyan frosted diffuser", bevel=0.003, rot=(0, math.pi / 4, 0))
rounded_box("roof", (2.45, 0.38, 0.1), (0, 0.0, 3.1), OR, radius=0.05)
for sx in (-1, 1):
    box(f"roof lamp {sx}", (0.5, 0.06, 0.04), (sx * 0.6, -0.15, 3.04), "mars:lamp", bevel=0.01)
sphere("beacon", (0, 0.05, 3.25), 0.08, CY)
empty("anchor_screen", (0, -0.125, 2.35), (0, -1, 0))
box("COL_body", (2.4, 0.5, 3.2), (0, 0.05, 1.6), GR, bevel=0)
save_and_preview(OUT, (0, 0, 1.7), 8, "Mars market board", views=((280, 20), (215, 18), (300, 8)))
print("DONE")
