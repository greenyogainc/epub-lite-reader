"""Tests for the generator scripts in tools/.

Module paths are redirected to tmp_path, so a run never rewrites a committed file.
"""

from __future__ import annotations

import zipfile
from pathlib import Path

import make_demo_epub
import make_icons
import make_localized_resx
import make_review_probe_epub
import pytest
from PIL import Image

REPO = Path(__file__).resolve().parents[2]


def read_text_normalized(path: Path) -> str:
    return path.read_text(encoding="utf-8").replace("\r\n", "\n")


@pytest.mark.parametrize("module", [make_demo_epub, make_localized_resx])
def test_escape_replaces_xml_metacharacters(module):
    assert module.escape("a & b <c> d") == "a &amp; b &lt;c&gt; d"


class TestDemoEpub:
    @pytest.fixture
    def outputs(self, tmp_path, monkeypatch):
        out = tmp_path / "nested" / "demo.epub"
        bundled = tmp_path / "assets" / "sample.epub"
        monkeypatch.setattr(make_demo_epub, "OUT", out)
        monkeypatch.setattr(make_demo_epub, "BUNDLED_OUT", bundled)
        return out, bundled

    def test_build_writes_a_spec_shaped_epub_to_both_locations(self, outputs):
        out, bundled = outputs

        make_demo_epub.build()

        assert bundled.read_bytes() == out.read_bytes()
        with zipfile.ZipFile(out) as epub:
            first = epub.infolist()[0]
            assert first.filename == "mimetype"
            assert first.compress_type == zipfile.ZIP_STORED
            assert epub.read("mimetype") == b"application/epub+zip"
            names = set(epub.namelist())
        assert {f"OEBPS/{cid}.xhtml" for cid, *_ in make_demo_epub.CHAPTERS} <= names

    def test_build_replaces_an_existing_output(self, outputs):
        out, _ = outputs
        out.parent.mkdir(parents=True)
        out.write_bytes(b"stale")

        make_demo_epub.build()

        assert zipfile.is_zipfile(out)

    def test_generator_matches_the_committed_sample_book(self, outputs):
        out, _ = outputs

        make_demo_epub.build()

        committed = REPO / "src" / "EpubLiteReader" / "Assets" / "sample.epub"
        with zipfile.ZipFile(out) as generated, zipfile.ZipFile(committed) as shipped:
            assert generated.namelist() == shipped.namelist()
            for name in generated.namelist():
                assert generated.read(name) == shipped.read(name), name

    def test_chapter_escapes_titles_and_paragraphs(self):
        xhtml = make_demo_epub.chapter_xhtml("c9", "A & B", "Part <1>", ["x < y"])

        assert "<h1>A &amp; B</h1>" in xhtml
        assert '<h2 class="kicker">Part &lt;1&gt;</h2>' in xhtml
        assert "<p>x &lt; y</p>" in xhtml

    def test_nav_groups_chapters_under_their_part(self, monkeypatch):
        monkeypatch.setattr(
            make_demo_epub,
            "CHAPTERS",
            [
                ("a", "First", "Part I", []),
                ("b", "Second", "Part I", []),
                ("c", "Third", "Part II", []),
            ],
        )

        nav = make_demo_epub.nav_xhtml()

        assert nav.count("<span>Part I</span>") == 1
        assert nav.count("<span>Part II</span>") == 1
        assert (
            nav.index('href="b.xhtml"')
            < nav.index("<span>Part II</span>")
            < nav.index('href="c.xhtml"')
        )

    def test_opf_lists_every_chapter_in_manifest_and_spine(self):
        opf = make_demo_epub.content_opf()

        for cid, *_ in make_demo_epub.CHAPTERS:
            assert f'<item id="{cid}" href="{cid}.xhtml"' in opf
            assert f'<itemref idref="{cid}"/>' in opf


class TestLocalizedResx:
    def test_generator_matches_the_committed_resx_files(self, tmp_path, monkeypatch):
        monkeypatch.setattr(make_localized_resx, "SRC", tmp_path)

        make_localized_resx.build()

        committed = REPO / "src" / "EpubLiteReader"
        for locale in make_localized_resx.TRANSLATIONS:
            name = f"Strings.{locale}.resx"
            assert read_text_normalized(tmp_path / name) == read_text_normalized(
                committed / name
            ), name

    def test_every_key_is_written_once_in_order_and_escaped(
        self, tmp_path, monkeypatch
    ):
        monkeypatch.setattr(make_localized_resx, "SRC", tmp_path)
        monkeypatch.setattr(make_localized_resx, "ORDER", ["B", "A"])
        monkeypatch.setattr(
            make_localized_resx, "TRANSLATIONS", {"xx": {"A": "a & b", "B": "<b>"}}
        )

        make_localized_resx.build()

        text = (tmp_path / "Strings.xx.resx").read_text(encoding="utf-8")
        assert text.index('name="B"') < text.index('name="A"')
        assert "<value>a &amp; b</value>" in text
        assert "<value>&lt;b&gt;</value>" in text

    def test_a_key_mismatch_stops_before_writing_anything(self, tmp_path, monkeypatch):
        monkeypatch.setattr(make_localized_resx, "SRC", tmp_path)
        monkeypatch.setattr(make_localized_resx, "ORDER", ["A", "B"])
        monkeypatch.setattr(
            make_localized_resx,
            "TRANSLATIONS",
            {
                "ok": {"A": "1", "B": "2"},
                "xx": {"A": "1", "C": "3"},
            },
        )

        with pytest.raises(SystemExit) as stopped:
            make_localized_resx.build()

        message = str(stopped.value)
        assert "xx: missing=['B'] extra=['C']" in message
        assert "ok:" not in message
        assert list(tmp_path.iterdir()) == []


class TestIcons:
    def test_build_renders_every_asset_at_its_size(self, tmp_path, monkeypatch):
        assets = tmp_path / "assets"
        ico = tmp_path / "ico" / "app.ico"
        monkeypatch.setattr(make_icons, "ASSETS", assets)
        monkeypatch.setattr(make_icons, "APP_ICO", ico)

        make_icons.build()

        for name, size in make_icons.SQUARE_TILES.items():
            with Image.open(assets / name) as tile:
                assert tile.size == (size, size), name
        with Image.open(assets / "Wide310x150Logo.png") as wide:
            assert wide.size == (310, 150)
        with Image.open(ico) as icon:
            assert icon.info["sizes"] == {(s, s) for s in make_icons.ICO_SIZES}

    def test_wide_tile_letterboxes_on_the_background_colour(self):
        wide = make_icons.wide(310, 150)

        assert wide.size == (310, 150)
        assert wide.getpixel((0, 75)) == make_icons.BG
        assert wide.getpixel((309, 75)) == make_icons.BG

    def test_build_stops_when_the_logo_is_missing(self, tmp_path, monkeypatch):
        monkeypatch.setattr(make_icons, "LOGO_SRC", tmp_path / "missing.png")
        monkeypatch.setattr(make_icons, "ASSETS", tmp_path / "assets")

        with pytest.raises(SystemExit, match="Logo not found"):
            make_icons.build()

        assert not (tmp_path / "assets").exists()


class TestReviewProbeEpub:
    def test_main_writes_the_probe_entries(self, tmp_path):
        out = tmp_path / "probe.epub"

        make_review_probe_epub.main(str(out))

        with zipfile.ZipFile(out) as epub:
            first = epub.infolist()[0]
            assert first.filename == "mimetype"
            assert first.compress_type == zipfile.ZIP_STORED
            assert "OEBPS/C#.xhtml" in epub.namelist()
            assert b"<script>" in epub.read("OEBPS/evil.shtm")
            assert b"int one = 1;" in epub.read("OEBPS/chapter1.xhtml")
