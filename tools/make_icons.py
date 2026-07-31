"""Generate simple MSIX tile assets and refresh app.ico from a solid brand mark."""
from __future__ import annotations

import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "packaging" / "Assets"
ASSETS.mkdir(parents=True, exist_ok=True)

# Brand: deep slate + reading-blue accent (matches toolbar accent #1F5B94)
BG = (0x2B, 0x2B, 0x2B, 255)
ACCENT = (0x1F, 0x5B, 0x94, 255)
FG = (0xDD, 0xDD, 0xDD, 255)


def png(width: int, height: int, rgba_rows: list[bytes]) -> bytes:
    def chunk(tag: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + row for row in rgba_rows)
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def fill(w: int, h: int, color: tuple[int, int, int, int]) -> list[bytes]:
    px = bytes(color)
    return [px * w for _ in range(h)]


def draw_book(w: int, h: int) -> list[bytes]:
    rows = fill(w, h, BG)
    # Page rectangle
    x0, y0 = int(w * 0.22), int(h * 0.18)
    x1, y1 = int(w * 0.78), int(h * 0.82)
    mid = (x0 + x1) // 2
    for y in range(h):
        row = bytearray(rows[y])
        for x in range(w):
            if x0 <= x <= x1 and y0 <= y <= y1:
                # spine
                if abs(x - mid) <= max(1, w // 40):
                    row[x * 4 : x * 4 + 4] = bytes(ACCENT)
                else:
                    row[x * 4 : x * 4 + 4] = bytes(FG)
                # margin frame
                if x in (x0, x1) or y in (y0, y1):
                    row[x * 4 : x * 4 + 4] = bytes(ACCENT)
        rows[y] = bytes(row)
    return rows


def write_png(path: Path, w: int, h: int) -> None:
    path.write_bytes(png(w, h, draw_book(w, h)))
    print("wrote", path)


def main() -> None:
    sizes = {
        "StoreLogo.png": (50, 50),
        "Square44x44Logo.png": (44, 44),
        "Square150x150Logo.png": (150, 150),
        "Wide310x150Logo.png": (310, 150),
        "Square310x310Logo.png": (310, 310),
    }
    for name, (w, h) in sizes.items():
        write_png(ASSETS / name, w, h)


if __name__ == "__main__":
    main()
