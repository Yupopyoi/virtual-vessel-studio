"""Unit tests for repo_checks.py. Run from the repository root:  python -m unittest discover -s tools/ci"""

import unittest

import repo_checks

ROOT = "unity/P/Assets"


class MetaFileTests(unittest.TestCase):
    def test_assets_and_folders_with_meta_pass(self):
        tracked = [
            f"{ROOT}/A.meta",
            f"{ROOT}/A/x.cs",
            f"{ROOT}/A/x.cs.meta",
        ]
        self.assertEqual(repo_checks.check_meta_files(tracked, ROOT), [])

    def test_missing_meta_for_a_file_and_its_folder(self):
        tracked = [f"{ROOT}/A/x.cs"]
        self.assertEqual(
            repo_checks.check_meta_files(tracked, ROOT),
            [f"Missing .meta: {ROOT}/A", f"Missing .meta: {ROOT}/A/x.cs"],
        )

    def test_orphaned_meta(self):
        tracked = [f"{ROOT}/gone.cs.meta"]
        self.assertEqual(
            repo_checks.check_meta_files(tracked, ROOT),
            [f"Orphaned .meta (no asset or folder): {ROOT}/gone.cs.meta"],
        )

    def test_hidden_and_tilde_names_need_no_meta(self):
        tracked = [f"{ROOT}/.hidden/x.txt", f"{ROOT}/Docs~/readme.md"]
        self.assertEqual(repo_checks.check_meta_files(tracked, ROOT), [])

    def test_files_outside_assets_are_ignored(self):
        self.assertEqual(repo_checks.check_meta_files(["docs/ja/a.md", "unity/P/Packages/manifest.json"], ROOT), [])


class GuidTests(unittest.TestCase):
    def test_duplicate_guid(self):
        guid = "0123456789abcdef0123456789abcdef"
        metas = {"a.meta": f"fileFormatVersion: 2\nguid: {guid}\n", "b.meta": f"guid: {guid.upper()}\n"}
        self.assertEqual(repo_checks.check_duplicate_guids(metas), [f"Duplicate GUID {guid}: a.meta, b.meta"])

    def test_distinct_guids(self):
        metas = {"a.meta": "guid: " + "1" * 32, "b.meta": "guid: " + "2" * 32}
        self.assertEqual(repo_checks.check_duplicate_guids(metas), [])


class StringTableTests(unittest.TestCase):
    def test_same_keys_pass(self):
        tables = {"ja.json": '{"strings": {"a": "あ"}}', "en.json": '{"strings": {"a": "A"}}'}
        self.assertEqual(repo_checks.check_string_tables(tables), [])

    def test_missing_key_is_reported_for_the_file_without_it(self):
        tables = {"ja.json": '{"strings": {"a": "あ", "b": "い"}}', "en.json": '{"strings": {"a": "A"}}'}
        self.assertEqual(repo_checks.check_string_tables(tables), ["Missing key in en.json: b"])

    def test_invalid_json_and_missing_strings(self):
        tables = {"ja.json": "{", "en.json": '{"locale": "en"}'}
        errors = repo_checks.check_string_tables(tables)
        self.assertEqual(len(errors), 2)
        self.assertTrue(errors[0].startswith('No "strings" object in en.json'))
        self.assertTrue(errors[1].startswith("Invalid JSON in ja.json"))


class DocPairTests(unittest.TestCase):
    def test_pairs_pass_and_assets_are_ignored(self):
        tracked = ["docs/ja/ui/a.md", "docs/en/ui/a.md", "docs/assets/ui/styleboard.html"]
        self.assertEqual(repo_checks.check_doc_pairs(tracked), [])

    def test_missing_translation(self):
        tracked = ["docs/ja/ui/a.md", "docs/ja/ui/b.md", "docs/en/ui/a.md"]
        self.assertEqual(repo_checks.check_doc_pairs(tracked), ["Missing docs/en/ui/b.md"])


if __name__ == "__main__":
    unittest.main()
