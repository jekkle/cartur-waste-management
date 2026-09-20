"""High-res grey waste bin for Cartur's Waste Management.

Drawn from geometry, not traced: the reference bin is another author's sprite, so the
shape here is rebuilt from ellipses and a taper and the recycling mark is constructed
from Gary Anderson's public-domain design. Nothing is sampled from their file.

Authored at 8x and reduced, so the 128 px icon has clean edges and the 64 px one still
reads.
"""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 8
N = 128 * S
CX = N / 2
TOP_Y, BOT_Y = int(0.20 * N), int(0.855 * N)
RX_TOP, RY_TOP = int(0.335 * N), int(0.105 * N)
RX_BOT, RY_BOT = int(0.265 * N), int(0.075 * N)
RIM = int(0.022 * N)


def mask_body():
    m = Image.new("L", (N, N), 0)
    d = ImageDraw.Draw(m)
    d.polygon([(CX - RX_TOP, TOP_Y), (CX + RX_TOP, TOP_Y), (CX + RX_BOT, BOT_Y), (CX - RX_BOT, BOT_Y)], fill=255)
    d.ellipse([CX - RX_BOT, BOT_Y - RY_BOT, CX + RX_BOT, BOT_Y + RY_BOT], fill=255)
    d.ellipse([CX - RX_TOP, TOP_Y - RY_TOP, CX + RX_TOP, TOP_Y + RY_TOP], fill=255)
    return m


def gradient_body():
    """Cylinder shading: brightest a third in from the left, falling off to both rims,
    with a slow vertical darkening so the bin has weight at the foot."""
    x = np.linspace(0, 1, N)[None, :]
    y = np.linspace(0, 1, N)[:, None]
    lat = 1 - np.abs((x - 0.36) / 0.62) ** 1.7
    tone = 96 + 92 * np.clip(lat, 0, 1)
    tone = tone * (1.0 - 0.20 * np.clip((y - 0.25) / 0.7, 0, 1))
    grain = np.random.default_rng(7).normal(0, 2.4, (N, N))
    grain += np.sin(x * N * 0.9) * 1.6           # faint vertical brushing
    return np.clip(tone + grain, 0, 255)


def mobius(size, col=(18, 18, 18, 255)):
    """Three folded arrows on a triangle. Each arrow is a band along one side with a
    slanted tail and a chevron head, rotated 120 degrees twice."""
    n = size
    im = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    c = n / 2
    r = n * 0.40
    w = n * 0.155

    def rot(p, deg):
        a = math.radians(deg)
        x, y = p[0] - c, p[1] - c
        return (c + x * math.cos(a) - y * math.sin(a), c + x * math.sin(a) + y * math.cos(a))

    V = [(c + r * math.cos(math.radians(a - 90)), c + r * math.sin(math.radians(a - 90))) for a in (0, 120, 240)]
    ax, ay = V[0]
    bx, by = V[1]
    dx, dy = bx - ax, by - ay
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    px, py = -uy, ux

    def P(t, off):
        return (ax + ux * L * t + px * off, ay + uy * L * t + py * off)

    arrow = [P(0.02, -w * 0.15), P(0.50, -w * 0.15), P(0.50, -w * 0.95),
             P(0.80, w * 0.35), P(0.50, w * 1.55), P(0.50, w * 0.80), P(0.16, w * 0.80)]
    for k in range(3):
        d.polygon([rot(p, k * 120) for p in arrow], fill=col)
    return im


def build(with_mark=True, out="bin_hd.png"):
    body = mask_body()
    tone = gradient_body()
    rgb = np.dstack([tone, tone * 0.995, tone * 0.985]).astype(np.uint8)
    im = Image.fromarray(np.dstack([rgb, np.asarray(body)]).astype(np.uint8), "RGBA")
    d = ImageDraw.Draw(im)

    # inner wall, seen through the mouth: dark, lighter where the far wall catches light
    inner = [CX - RX_TOP + RIM, TOP_Y - RY_TOP + int(RIM * 0.55), CX + RX_TOP - RIM, TOP_Y + RY_TOP - int(RIM * 0.55)]
    d.ellipse(inner, fill=(26, 26, 27, 255))
    d.ellipse([inner[0] + RIM * 1.6, inner[1] + RIM * 0.9, inner[2] - RIM * 1.6, inner[1] + RY_TOP * 1.1], fill=(46, 46, 48, 255))

    if with_mark:
        size = int(N * 0.34)
        m = mobius(size).resize((int(size * 0.92), size), Image.LANCZOS)
        im.alpha_composite(m, (int(CX - m.width / 2), int(N * 0.40)))

    # thin bright edge all the way round, the way the reference reads at small size
    edge = body.filter(ImageFilter.MaxFilter(3))
    edge = Image.fromarray((np.asarray(edge).astype(np.int16) - np.asarray(body).astype(np.int16)).clip(0, 255).astype(np.uint8))
    im.alpha_composite(Image.fromarray(np.dstack([np.full((N, N), 205), np.full((N, N), 205), np.full((N, N), 200), np.asarray(edge)]).astype(np.uint8), "RGBA"))

    for px in (256, 128, 64):
        im.resize((px, px), Image.LANCZOS).save(out.replace(".png", f"_{px}.png"))


build(True, "bin_mark.png")
build(False, "bin_plain.png")
print("ok")
