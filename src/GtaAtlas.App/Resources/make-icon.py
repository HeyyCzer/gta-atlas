"""Generates atlas.ico (16-256 px). Run: python make-icon.py"""
from PIL import Image, ImageDraw

BG = (0x1F, 0x20, 0x26, 255)
EDGE = (0x34, 0x36, 0x3F, 255)
TEAL = (0x3F, 0xB8, 0xAF, 255)
LIGHT = (0xE6, 0xE6, 0xE6, 255)


def draw(size):
    # Small sizes get thicker strokes so the globe stays legible.
    s = 1024
    stroke = 70 if size <= 24 else 56 if size <= 48 else 40
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((16, 16, s - 16, s - 16), radius=200, fill=BG, outline=EDGE, width=16)

    c, r = s // 2, 330
    box = (c - r, c - r, c + r, c + r)
    d.ellipse(box, outline=TEAL, width=stroke)
    # Meridian and parallels.
    d.ellipse((c - r * 0.42, c - r, c + r * 0.42, c + r), outline=TEAL, width=stroke * 3 // 4)
    d.line((c - r, c, c + r, c), fill=TEAL, width=stroke * 3 // 4)
    if size >= 32:
        for dy in (-0.55, 0.55):
            y = c + r * dy
            half = (r * r - (r * dy) ** 2) ** 0.5
            d.line((c - half + 20, y, c + half - 20, y), fill=TEAL, width=stroke // 2)

    # Location pin on the upper right, the "atlas" marker.
    px, py, pr = c + r * 0.62, c - r * 0.62, 120 if size >= 32 else 150
    d.ellipse((px - pr, py - pr, px + pr, py + pr), fill=LIGHT, outline=BG, width=36)
    d.ellipse((px - pr * 0.38, py - pr * 0.38, px + pr * 0.38, py + pr * 0.38), fill=BG)
    return img.resize((size, size), Image.LANCZOS)


sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
frames = [draw(n) for n in sizes]
frames[-1].save("atlas.ico", sizes=[(n, n) for n in sizes], append_images=frames[:-1])
