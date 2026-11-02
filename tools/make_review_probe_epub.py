#!/usr/bin/env python3
"""Builds review-probe.epub: a benign fixture for the 2026-09-26 review findings.

Every probe only rewrites a marker <span> in the book's own page (no network,
no host messages). Open it in EPUB Lite Reader on Windows:
  - "EXECUTED" in a marker = script ran inside the reader (F1/F2).
  - Section 3 shows whether code-sample text survives the sanitizer (F3).
  - Chapter 2 lives in "C#.xhtml"; press Next to reach it. It should render,
    not come up blank/404 (F5). Its TOC entry is not clickable: VersOne 3.3.6
    unescapes the nav href before splitting off the anchor, so "C%23.xhtml"
    parses as file "C" + anchor ".xhtml" (upstream quirk, not the reader's bug).
  - Section 3 in an XHTML chapter currently ends in Chromium's "This page
    contains the following errors" banner: the sanitizer ate "</code".
"""
import sys
import zipfile

MARK = "document.getElementById('{id}').textContent='EXECUTED'"

CH1 = f"""<?xml version="1.0" encoding="utf-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:xlink="http://www.w3.org/1999/xlink">
<head><title>Review probe</title></head>
<body>
<h1>Review probe (2026-09-26)</h1>
<h2>1. No interaction needed</h2>
<p>iframe src=javascript: <span id="m1">not run</span></p>
<iframe style="display:none" src="javascript:parent.{MARK.format(id='m1')}"></iframe>
<p>mislabeled evil.shtm (declared image/png) <span id="m2">not run</span></p>
<iframe style="display:none" src="evil.shtm"></iframe>

<h2>2. Click each link (only the first one should do nothing)</h2>
<p><a href="javascript:{MARK.format(id='c0')}">baseline href=javascript:</a> <span id="c0">not run</span></p>
<p><a href=" javascript:{MARK.format(id='c1')}">leading space</a> <span id="c1">not run</span></p>
<p><a href="java&#9;script:{MARK.format(id='c2')}">tab inside scheme</a> <span id="c2">not run</span></p>
<p><svg xmlns="http://www.w3.org/2000/svg" width="220" height="24"><a xlink:href="javascript:{MARK.format(id='c3')}"><text x="0" y="18" fill="blue">SVG xlink:href link</text></a></svg> <span id="c3">not run</span></p>
<form action="javascript:{MARK.format(id='c4')}"><p><button type="submit">form action</button> <span id="c4">not run</span></p></form>
<form><p><button type="submit" formaction="javascript:{MARK.format(id='c5')}">button formaction</button> <span id="c5">not run</span></p></form>

<h2>3. Code sample text (should read exactly as written)</h2>
<pre><code>int one = 1;
bool online = false;
var onTimeout = cb;</code></pre>
<p>Expected three lines: "int one = 1;", "bool online = false;", "var onTimeout = cb;"</p>
</body></html>
"""

CH2 = """<?xml version="1.0" encoding="utf-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml"><head><title>C#</title></head>
<body><h1>Chapter 2 rendered</h1><p>If you can read this, file names containing '#' load.</p></body></html>
"""

SHTM = "x<script>parent.document.getElementById('m2').textContent='EXECUTED'</script>"

NAV = """<?xml version="1.0" encoding="utf-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
<head><title>Contents</title></head>
<body><nav epub:type="toc"><ol>
<li><a href="chapter1.xhtml">Probes</a></li>
<li><a href="C%23.xhtml">Chapter 2 (C#.xhtml)</a></li>
</ol></nav></body></html>
"""

OPF = """<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="uid">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="uid">urn:uuid:3f0a6a3e-1d0e-4c55-9a0b-review-probe</dc:identifier>
    <dc:title>EPUB Lite Reader review probe</dc:title>
    <dc:language>en</dc:language>
    <meta property="dcterms:modified">2026-09-26T00:00:00Z</meta>
  </metadata>
  <manifest>
    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
    <item id="c1" href="chapter1.xhtml" media-type="application/xhtml+xml" properties="svg"/>
    <item id="c2" href="C%23.xhtml" media-type="application/xhtml+xml"/>
    <item id="evil" href="evil.shtm" media-type="image/png"/>
  </manifest>
  <spine>
    <itemref idref="c1"/>
    <itemref idref="c2"/>
  </spine>
</package>
"""

CONTAINER = """<?xml version="1.0" encoding="utf-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
</container>
"""


def main(out: str) -> None:
    with zipfile.ZipFile(out, "w") as z:
        z.writestr(zipfile.ZipInfo("mimetype"), "application/epub+zip", compress_type=zipfile.ZIP_STORED)
        entries = {
            "META-INF/container.xml": CONTAINER,
            "OEBPS/content.opf": OPF,
            "OEBPS/nav.xhtml": NAV,
            "OEBPS/chapter1.xhtml": CH1,
            "OEBPS/C#.xhtml": CH2,
            "OEBPS/evil.shtm": SHTM,
        }
        for name, body in entries.items():
            z.writestr(name, body.encode("utf-8"), compress_type=zipfile.ZIP_DEFLATED)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "review-probe.epub")
