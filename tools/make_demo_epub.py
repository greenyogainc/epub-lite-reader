"""Build a small original-content demo EPUB for Store screenshots.

Not a test fixture (see tools/fixtures/) -- this is presentation content:
enough chapters, nested headings, and prose to make facing-page, continuous-
scroll, and chapter-sidebar screenshots look like a real book.
"""
from __future__ import annotations

import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "packaging" / "store-screenshots" / "source" / "EpubLiteReader-demo.epub"

TITLE = "A Quiet Path"
AUTHOR = "Green Yoga Inc"

# (id, title, part, paragraphs)
CHAPTERS = [
    ("c1", "Setting Out", "Part One: The Trailhead", [
        "The trail begins where the gravel road gives up and the grass takes over, "
        "a thin seam of packed earth running north between two stands of aspen. "
        "There is no sign, no marker, only the shape of the ground telling you "
        "that other feet have gone this way before.",
        "I like to leave early, before the light has fully decided what kind of "
        "day it will be. The air still holds the cold of the night in its lower "
        "layers, pooled in the hollows like water, and it is possible, for the "
        "first mile, to walk through several different seasons at once.",
        "A trip like this does not need a destination so much as a direction. "
        "North, and slightly up. That is enough of a plan for the first hour.",
    ]),
    ("c2", "The First Ridge", "Part One: The Trailhead", [
        "By the second mile the trail stops pretending to be flat. It climbs in "
        "long diagonal switchbacks through a forest that thins as it rises, the "
        "pines growing more spaced out and more stubborn the higher they go.",
        "From the first ridge you can look back and see exactly how far the "
        "morning has taken you, which is always less than it felt like and more "
        "than it looks like it should be. The road is a pale thread far below, "
        "and the town beyond it is just a scatter of roofs catching the sun.",
        "There is a kind of quiet up here that has weight to it. Not silence -- "
        "wind moves through the pines constantly, and somewhere a creek is "
        "working its way downhill -- but a quiet that makes you lower your own "
        "voice without deciding to.",
    ]),
    ("c3", "Above the Treeline", "Part Two: Higher Ground", [
        "The trees end abruptly, as if someone had drawn a line across the "
        "mountainside and told them to stop. Above it, the ground turns to "
        "grass, then rock, then something that is mostly rock with opinions "
        "about where grass is allowed.",
        "The wind is different here too -- constant, unbothered by the small "
        "obstacles that used to slow it down in the forest. It has room to build "
        "up speed for miles before it reaches you, and it does not let you "
        "forget that.",
        "Walking above the treeline changes how far you can see, which changes "
        "how far you feel like you have gone. Distances stop being measured in "
        "steps and start being measured in ridgelines.",
    ]),
    ("c4", "The Old Cabin", "Part Two: Higher Ground", [
        "It sits in a shallow bowl just below the pass, roof half caved in on "
        "the north side, the kind of structure that has clearly stopped being "
        "useful to anyone but has not yet gotten around to disappearing.",
        "Someone built a fireplace here once, stone by stone, hauled up from "
        "somewhere lower down where stones like that are easier to find. It is "
        "still mostly standing, which is more than can be said for the walls "
        "around it.",
        "I sit on what used to be a doorstep and eat lunch looking out at the "
        "pass, and it is easy, for a few minutes, to imagine this place as it "
        "was when the roof still kept the weather out.",
    ]),
    ("c5", "Coming Down", "Part Two: Higher Ground", [
        "The way down is always faster and always harder on the knees, a trade "
        "that seems fair in the morning and less fair by the last mile.",
        "You notice different things going down than you did going up -- a rock "
        "formation you walked right past, a side trail you did not have the "
        "curiosity for earlier, the particular way the light falls through the "
        "aspens in the late afternoon instead of the early morning.",
        "By the time the trail flattens back into gravel road, the day has "
        "turned into exactly the kind of day it was always going to be, and the "
        "only thing left to do is remember it correctly.",
    ]),
]

STYLE = """
body { font-family: serif; margin: 1em 1.5em; line-height: 1.5; }
h1 { font-size: 1.4em; margin-bottom: 0.2em; }
h2.kicker { font-size: 0.85em; letter-spacing: 0.08em; text-transform: uppercase;
            color: #666; margin: 0 0 1.2em 0; font-weight: normal; }
p { margin: 0 0 1em 0; text-align: justify; }
""".strip()

MIMETYPE = "application/epub+zip"


def escape(s: str) -> str:
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")

CONTAINER_XML = """<?xml version="1.0"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles>
    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
  </rootfiles>
</container>
"""


def chapter_xhtml(cid: str, title: str, part: str, paragraphs: list[str]) -> str:
    title, part = escape(title), escape(part)
    body = "\n    ".join(f"<p>{escape(p)}</p>" for p in paragraphs)
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml">
<head><title>{title}</title><link rel="stylesheet" href="style.css"/></head>
<body>
  <h2 class="kicker">{part}</h2>
  <h1>{title}</h1>
    {body}
</body>
</html>
"""


def nav_xhtml() -> str:
    parts: dict[str, list[tuple[str, str]]] = {}
    for cid, title, part, _ in CHAPTERS:
        parts.setdefault(part, []).append((cid, title))

    items = []
    for part, chapters in parts.items():
        inner = "\n      ".join(
            f'<li><a href="{cid}.xhtml">{escape(title)}</a></li>' for cid, title in chapters
        )
        items.append(f"<li><span>{escape(part)}</span><ol>\n      {inner}\n    </ol></li>")
    toc = "\n    ".join(items)

    return f"""<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
<head><title>Contents</title></head>
<body>
  <nav epub:type="toc"><ol>
    {toc}
  </ol></nav>
</body>
</html>
"""


def content_opf() -> str:
    manifest_items = "\n    ".join(
        f'<item id="{cid}" href="{cid}.xhtml" media-type="application/xhtml+xml"/>'
        for cid, _, _, _ in CHAPTERS
    )
    spine_items = "\n    ".join(f'<itemref idref="{cid}"/>' for cid, _, _, _ in CHAPTERS)
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="3.0" xml:lang="en">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="BookId">urn:uuid:elr-demo-001</dc:identifier>
    <dc:title>{escape(TITLE)}</dc:title>
    <dc:creator>{escape(AUTHOR)}</dc:creator>
    <dc:language>en</dc:language>
  </metadata>
  <manifest>
    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
    {manifest_items}
    <item id="css" href="style.css" media-type="text/css"/>
  </manifest>
  <spine>
    {spine_items}
  </spine>
</package>
"""


def build() -> None:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    if OUT.exists():
        OUT.unlink()

    with zipfile.ZipFile(OUT, "w") as zf:
        # mimetype must be first and stored (uncompressed), per the EPUB spec.
        zf.writestr("mimetype", MIMETYPE, compress_type=zipfile.ZIP_STORED)
        zf.writestr("META-INF/container.xml", CONTAINER_XML, compress_type=zipfile.ZIP_DEFLATED)
        zf.writestr("OEBPS/style.css", STYLE, compress_type=zipfile.ZIP_DEFLATED)
        zf.writestr("OEBPS/nav.xhtml", nav_xhtml(), compress_type=zipfile.ZIP_DEFLATED)
        zf.writestr("OEBPS/content.opf", content_opf(), compress_type=zipfile.ZIP_DEFLATED)
        for cid, title, part, paragraphs in CHAPTERS:
            zf.writestr(
                f"OEBPS/{cid}.xhtml",
                chapter_xhtml(cid, title, part, paragraphs),
                compress_type=zipfile.ZIP_DEFLATED,
            )

    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    build()
