"""Builds the trash can icon for Cartur's Waste Management.

    python tools/art/make_icon.py          # renders into tools/art/out/
    python tools/art/make_icon.py --install # and copies into src/Assets/

Two halves:

  gen_hd.py  draws the bin itself from geometry - two ellipses and a taper, cylinder
             shading brightest a third in from the left, faint vertical brushing, dark
             mouth with a lighter far wall, thin bright edge. Nothing is sampled from
             another mod's sprite.

  cutout.py  lifts the three-arrow mark out of mark_source.jpg. The mark is Gary
             Anderson's 1970 design, public domain; the jpg is a flat two-colour
             rendering of it, so the mask is the public shape rather than an artist's
             styling. It is upscaled and re-thresholded because the source is small
             and blocky.

The mark sits at 100/256 wide, 100 px down, squeezed to 93% width so it wraps the
barrel instead of lying flat.
"""
import os
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(HERE, "out")
ASSETS = os.path.join(ROOT, "src", "Assets")

MARK_W, MARK_Y, CURVE = 100, 100, 0.93
INK = (24, 24, 25)

os.makedirs(OUT, exist_ok=True)
for script in ("gen_hd.py", "cutout.py"):
    subprocess.run([sys.executable, os.path.join(HERE, script)], cwd=OUT, check=True)

bin_img = Image.open(os.path.join(OUT, "bin_plain_256.png")).convert("RGBA")
mark = Image.open(os.path.join(OUT, "mark_cut.png"))

h = int(MARK_W * mark.height / mark.width)
mark = mark.resize((int(MARK_W * CURVE), h), Image.LANCZOS)
layer = Image.new("RGBA", mark.size, INK + (255,))
layer.putalpha(mark)
bin_img.alpha_composite(layer, ((bin_img.width - mark.width) // 2, MARK_Y))

for px in (256, 128, 64):
    bin_img.resize((px, px), Image.LANCZOS).save(os.path.join(OUT, f"trashcan_{px}.png"))

if "--install" in sys.argv:
    os.makedirs(ASSETS, exist_ok=True)
    for px in (256, 128, 64):
        src = os.path.join(OUT, f"trashcan_{px}.png")
        Image.open(src).save(os.path.join(ASSETS, f"trashcan_{px}.png"))
    print("installed into", ASSETS)
print("done")
