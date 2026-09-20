"""Lift the arrows out of the reference jpg instead of rebuilding them.

The mark itself is Gary Anderson's public-domain design; the jpg is just a flat
two-colour rendering of it, so isolating the white shape gives the design back rather
than someone's original artwork. The jpg is small and blocky, so the mask is cleaned
up and re-rendered at 4x rather than pasted as-is.
"""
import os
import numpy as np
from PIL import Image, ImageFilter

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mark_source.jpg")
im = Image.open(SRC).convert("RGB")
a = np.asarray(im).astype(np.int16)
R, G, B = a[:, :, 0], a[:, :, 1], a[:, :, 2]

green = (G > R + 18) & (G > B + 18)                     # the disc
ys, xs = np.nonzero(green)
y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()

# inside the disc's bounding box, the light pixels are the arrows
sub = a[y0:y1 + 1, x0:x1 + 1]
light = (sub.min(axis=2) > 120) & (np.abs(sub[:, :, 0] - sub[:, :, 1]) < 45)

m = Image.fromarray((light * 255).astype(np.uint8), "L")
# jpg noise: blur, threshold, then blur again for an anti-aliased edge at 4x
m = m.resize((m.width * 4, m.height * 4), Image.LANCZOS).filter(ImageFilter.GaussianBlur(3))
m = m.point(lambda v: 255 if v > 140 else 0).filter(ImageFilter.GaussianBlur(1.6))

# drop the white ring around the disc: keep only the biggest blob cluster near centre
arr = np.asarray(m)
h, w = arr.shape
yy, xx = np.mgrid[0:h, 0:w]
r = np.hypot(yy - h / 2, xx - w / 2)
arr = np.where(r < min(h, w) * 0.46, arr, 0)

m = Image.fromarray(arr.astype(np.uint8), "L")
bb = m.getbbox()
m = m.crop(bb)
m.save("mark_cut.png")
print("cut", m.size)
