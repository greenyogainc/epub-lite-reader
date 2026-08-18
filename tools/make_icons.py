"""Draw the app mark and derive every MSIX asset and app.ico from it.

The mark is the reading-blue book on slate, with the Green Yoga Inc logo as a
badge on the right. Only the logo is a source asset (packaging/gy-logo.png);
the book itself is drawn here, so every size is rendered rather than upscaled.
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
LOGO_SRC = ROOT / "packaging" / "gy-logo.png"
ASSETS = ROOT / "packaging" / "Assets"
APP_ICO = ROOT / "src" / "EpubLiteReader" / "app.ico"

# Brand: deep slate + reading-blue accent (matches toolbar accent #1F5B94)
BG = (0x2B, 0x2B, 0x2B, 255)
ACCENT = (0x1F, 0x5B, 0x94, 255)
FG = (0xDD, 0xDD, 0xDD, 255)

SS = 4               # supersample factor, removed by the final LANCZOS pass
BADGE_FRAC = 0.27    # logo width as a fraction of the icon
BADGE_PAD = 0.04
DISC_MARGIN = 0.11   # white disc ring around the logo, as a fraction of it

SQUARE_TILES = {
    "Square44x44Logo.png": 44,
    "Square150x150Logo.png": 150,
    "Square310x310Logo.png": 310,
    "StoreLogo.png": 50,
    # Shown on .epub files in Explorer via the file-type association.
    "EpubFileLogo.png": 256,
}

ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def render(size: int) -> Image.Image:
    """The full mark at `size` px, drawn at SSx and downsampled."""
    w = size * SS
    im = Image.new("RGBA", (w, w), BG)
    d = ImageDraw.Draw(im)

    x0, y0, x1, y1 = w * 0.22, w * 0.18, w * 0.78, w * 0.82
    d.rectangle([x0, y0, x1, y1], fill=FG, outline=ACCENT, width=max(SS, int(w * 0.006)))
    half = max(1, int(w / 40))
    mid = (x0 + x1) / 2
    d.rectangle([mid - half, y0, mid + half, y1], fill=ACCENT)

    # The logo is a deep green that muddies against both the slate ground and
    # the page; the white disc keeps it readable down to 44px.
    logo = Image.open(LOGO_SRC).convert("RGBA")
    s = int(w * BADGE_FRAC)
    logo = logo.resize((s, s), Image.LANCZOS)
    pad = int(w * BADGE_PAD)
    x, y = w - s - pad, w - s - pad
    m = int(s * DISC_MARGIN)
    d.ellipse([x - m, y - m, x + s + m, y + s + m], fill=(255, 255, 255, 255))
    im.alpha_composite(logo, (x, y))

    return im.resize((size, size), Image.LANCZOS)


def wide(w: int, h: int) -> Image.Image:
    """Letterbox the square mark onto the wide tile rather than distorting it."""
    canvas = Image.new("RGBA", (w, h), BG)
    art = render(h)
    canvas.alpha_composite(art, ((w - h) // 2, 0))
    return canvas


def build() -> None:
    if not LOGO_SRC.exists():
        raise SystemExit(f"Logo not found: {LOGO_SRC}")
    ASSETS.mkdir(parents=True, exist_ok=True)

    for name, size in SQUARE_TILES.items():
        out = ASSETS / name
        render(size).save(out)
        print(f"Wrote {out} ({size}x{size})")

    out = ASSETS / "Wide310x150Logo.png"
    wide(310, 150).save(out)
    print(f"Wrote {out} (310x150)")

    # Each .ico frame is rendered at its own size, not downscaled from one image.
    frames = [render(s) for s in ICO_SIZES]
    APP_ICO.parent.mkdir(parents=True, exist_ok=True)
    frames[-1].save(APP_ICO, format="ICO", sizes=[(s, s) for s in ICO_SIZES],
                    append_images=frames[:-1])
    print(f"Wrote {APP_ICO} ({', '.join(str(s) for s in ICO_SIZES)})")


if __name__ == "__main__":
    build()
