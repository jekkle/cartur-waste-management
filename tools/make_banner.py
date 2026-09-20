"""Draws the Nexus images: a 1300x372 page header and a 1920x1080 gallery shot.

Nexus wants a wide header and a 16:9 gallery image, not the square Thunderstore icon,
so the can is placed square on the left with the title beside it. Same shape as the
Flooring banner, and for the same reason: everything is measured as a fraction of the
height, so both sizes are one picture at two shapes.

The can itself is not redrawn here - it is tools/art/out/trashcan_256.png, the same
render the icon and the in-game sprite come from.

    python tools/make_banner.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
MEDIA = os.path.join(ROOT, "media")
CAN = os.path.join(HERE, "art", "out", "trashcan_256.png")

BACKGROUND = (24, 23, 22, 255)
TITLE = (238, 232, 220, 255)
ACCENT = (206, 162, 94, 255)
SUB = (150, 140, 124, 255)
SCALE = 2


def font(path, size):
    try:
        return ImageFont.truetype(path, size)
    except OSError:
        return ImageFont.load_default()


def render(w, h, out):
    image = Image.new("RGBA", (w * SCALE, h * SCALE), BACKGROUND)

    side = int(h * SCALE * 0.86)
    can = Image.open(CAN).convert("RGBA").resize((side, side), Image.LANCZOS)
    image.alpha_composite(can, (int(h * SCALE * 0.09), int(h * SCALE * 0.07)))

    draw = ImageDraw.Draw(image)
    title = font("C:/Windows/Fonts/segoeuib.ttf", int(h * SCALE * 0.103))
    sub = font("C:/Windows/Fonts/segoeui.ttf", int(h * SCALE * 0.047))

    x = int(h * SCALE * 1.06)
    draw.text((x, int(h * SCALE * 0.36)), "Cartur's", font=title, fill=TITLE)
    draw.text((x, int(h * SCALE * 0.47)), "Waste Management", font=title, fill=ACCENT)
    draw.text((x, int(h * SCALE * 0.61)), "A trash can and a sort button, where the mess is.",
              font=sub, fill=SUB)

    image.resize((w, h), Image.LANCZOS).convert("RGB").save(out)
    print("wrote", out, (w, h))


os.makedirs(MEDIA, exist_ok=True)
render(1300, 372, os.path.join(MEDIA, "nexus-header.png"))
render(1920, 1080, os.path.join(MEDIA, "nexus-gallery.png"))
