"""Derive every MSIX tile asset and app.ico from the master icon.

The master (packaging/icon-source.png) is hand-made square artwork; this script
is the only thing that writes packaging/Assets/*.png and app.ico, so the derived
assets are always reproducible. Re-run after replacing the master.
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "packaging" / "icon-source.png"
ASSETS = ROOT / "packaging" / "Assets"
APP_ICO = ROOT / "src" / "EpubLiteReader" / "app.ico"

# Square Store/tile assets, all straight downscales of the master.
SQUARE_TILES = {
    "Square44x44Logo.png": 44,
    "Square150x150Logo.png": 150,
    "Square310x310Logo.png": 310,
    "StoreLogo.png": 50,
}

# Windows picks from these; 20 and 40 matter for taskbar/title-bar crispness.
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def load_master() -> Image.Image:
    if not SOURCE.exists():
        raise SystemExit(f"Master icon not found: {SOURCE}")
    im = Image.open(SOURCE).convert("RGBA")
    if im.width != im.height:
        raise SystemExit(f"Master icon must be square, got {im.size}")
    return im


def square(im: Image.Image, size: int) -> Image.Image:
    return im.resize((size, size), Image.LANCZOS)


def wide(im: Image.Image, w: int, h: int) -> Image.Image:
    """Letterbox the square art onto a wide tile.

    Stretching to 310x150 would distort the artwork, so the icon is scaled to
    the tile height and centred on the master's own background colour, which
    makes the padding read as part of the tile rather than as bars.
    """
    bg = im.convert("RGB").getpixel((0, 0))
    canvas = Image.new("RGBA", (w, h), bg + (255,))
    art = square(im, h)
    canvas.paste(art, ((w - h) // 2, 0), art)
    return canvas


def build() -> None:
    master = load_master()
    ASSETS.mkdir(parents=True, exist_ok=True)

    for name, size in SQUARE_TILES.items():
        out = ASSETS / name
        square(master, size).save(out)
        print(f"Wrote {out} ({size}x{size})")

    out = ASSETS / "Wide310x150Logo.png"
    wide(master, 310, 150).save(out)
    print(f"Wrote {out} (310x150)")

    # Pillow downsamples each .ico frame from the master rather than from an
    # already-shrunk frame, so the small sizes stay as sharp as they can be.
    APP_ICO.parent.mkdir(parents=True, exist_ok=True)
    master.save(APP_ICO, format="ICO", sizes=[(s, s) for s in ICO_SIZES])
    print(f"Wrote {APP_ICO} ({', '.join(str(s) for s in ICO_SIZES)})")


if __name__ == "__main__":
    build()
