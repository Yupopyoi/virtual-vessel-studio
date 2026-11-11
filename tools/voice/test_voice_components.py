"""Tests for voice_components.py: the download list it builds from the sources and the local files."""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import voice_components  # noqa: E402

SOURCES = {
    "version": "1",
    "python": "3.12.2",
    "lock": "uv.lock",
    "mirror": "https://example.com/release/",
    "bases": {"hf": "https://hf.example/rev/"},
    "components": [
        {"id": "rmvpe", "url": "{hf}rmvpe.pt", "local": "{rvc}/rmvpe.pt", "license": "MIT", "mirrored": True,
         "place": "environment", "target": "rvc/rmvpe.pt"},
        {"id": "template", "url": "{mirror}t.sentis", "local": "{sentis}/t.sentis", "license": "MIT",
         "place": "environment", "target": "template/t.sentis"},
        {"id": "src", "url": "https://x/src.zip", "local": "{rvc}/src.zip", "license": "MIT", "place": "environment",
         "target": "rvc", "extract": "zip", "strip": 1},
    ],
}
FOLDERS = {"rvc": "R", "sentis": "S"}


class BuildTests(unittest.TestCase):
    def test_every_entry_takes_its_hash_and_size_from_the_local_file(self):
        listed = voice_components.build(SOURCES, FOLDERS, hasher=lambda p: "h:" + p, size_of=lambda p: len(p))

        rmvpe = listed["components"][0]
        self.assertEqual(rmvpe["url"], "https://hf.example/rev/rmvpe.pt")
        self.assertEqual(rmvpe["sha256"], "h:R/rmvpe.pt")
        self.assertEqual(rmvpe["mirror"], "https://example.com/release/rmvpe-rmvpe.pt")
        self.assertNotIn("mirror", listed["components"][1], "The project's own file is only in the Release.")
        self.assertEqual((listed["components"][2]["extract"], listed["components"][2]["strip"]), ("zip", 1))

    def test_the_release_holds_the_mirrors_and_the_projects_own_files(self):
        self.assertEqual(list(voice_components.release_files(SOURCES, FOLDERS)),
                         [("R/rmvpe.pt", "rmvpe-rmvpe.pt"), ("S/t.sentis", "t.sentis")])

    def test_an_unknown_placeholder_or_a_repeated_id_is_refused(self):
        with self.assertRaises(ValueError):
            voice_components.expand("{nope}x", {})
        twice = dict(SOURCES, components=[SOURCES["components"][0], SOURCES["components"][0]])
        with self.assertRaises(ValueError):
            voice_components.build(twice, FOLDERS, hasher=lambda p: "h", size_of=lambda p: 1)


if __name__ == "__main__":
    unittest.main()
