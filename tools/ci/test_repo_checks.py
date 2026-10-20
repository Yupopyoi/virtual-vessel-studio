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


class SbomTests(unittest.TestCase):
    LOCK = '{"dependencies": {"com.unity.ugui": {"version": "2.6.0"}, "com.unity.modules.ui": {"version": "1.0.0"}}}'
    WORKFLOW = "steps:\n  - uses: actions/checkout@v4\n  - uses: ./local-action\n"

    @staticmethod
    def component(reference, version="1", scope="required"):
        return {"bom-ref": reference, "name": reference, "version": version, "licenses": [{"license": {"id": "MIT"}}], "scope": scope}

    def sbom(self, *components):
        import json
        return json.dumps({"bomFormat": "CycloneDX", "components": list(components)})

    def required(self):
        tracked = ["unity/VirtualVesselStudio/Assets/ThirdParty/Font/Font.ttf", "unity/VirtualVesselStudio/Assets/ThirdParty/Font.meta"]
        return repo_checks.sbom_requirements(self.LOCK, tracked, {"ci.yml": self.WORKFLOW})

    def test_requirements_skip_modules_and_local_actions(self):
        self.assertEqual(self.required(), {"unity-package:com.unity.ugui": "2.6.0", "asset:Font": None, "github-action:actions/checkout@v4": None})

    def test_complete_sbom_passes(self):
        sbom = self.sbom(self.component("unity-package:com.unity.ugui", "2.6.0"), self.component("asset:Font"), self.component("github-action:actions/checkout@v4", scope="excluded"))
        self.assertEqual(repo_checks.check_sbom(sbom, self.required()), [])

    def test_missing_wrong_version_and_stale_entries(self):
        sbom = self.sbom(self.component("unity-package:com.unity.ugui", "2.5.0"), self.component("asset:Old"))
        errors = repo_checks.check_sbom(sbom, self.required())
        self.assertIn("SBOM: unity-package:com.unity.ugui is 2.5.0, but the repository uses 2.6.0", errors)
        self.assertIn("SBOM: missing asset:Font", errors)
        self.assertIn("SBOM: missing github-action:actions/checkout@v4", errors)
        self.assertIn("SBOM: asset:Old is listed but no longer used", errors)

    def test_an_embedded_package_needs_the_pinned_version(self):
        lock = '{"dependencies": {"com.example.plugin": {"version": "file:com.example.plugin", "source": "embedded"}}}'
        pinned = repo_checks.pinned_packages(['{"package": "com.example.plugin", "version": "1.2.3", "url": "u"}', '{"other": 1}'])
        self.assertEqual(pinned, {"com.example.plugin": "1.2.3"})
        self.assertEqual(repo_checks.sbom_requirements(lock, [], {}, pinned), {"unity-package:com.example.plugin": "1.2.3"})

    def test_a_pinned_package_is_required_before_it_is_fetched(self):
        required = repo_checks.sbom_requirements('{"dependencies": {}}', [], {}, {"com.example.plugin": "1.2.3"})
        self.assertEqual(required, {"unity-package:com.example.plugin": "1.2.3"})

    GIT_LOCK = '{"dependencies": {"com.example.git": {"version": "https://example.com/r.git?path=/P#v0.131.4", "source": "git"}}}'

    def test_a_git_package_needs_the_version_of_its_tag(self):
        self.assertEqual(repo_checks.sbom_requirements(self.GIT_LOCK, [], {}), {"unity-package:com.example.git": "0.131.4"})
        self.assertEqual(repo_checks.check_git_packages(self.GIT_LOCK), [])

    def test_a_git_package_without_a_tag_is_an_error(self):
        lock = '{"dependencies": {"com.example.git": {"version": "https://example.com/r.git?path=/P", "source": "git"}}}'
        self.assertEqual(len(repo_checks.check_git_packages(lock)), 1)

    def test_git_tag_versions(self):
        self.assertEqual(repo_checks.git_tag_version("u#v1.2.3"), "1.2.3")
        self.assertEqual(repo_checks.git_tag_version("u#release-1"), "release-1")
        self.assertIsNone(repo_checks.git_tag_version("u"))

    MANIFEST = '{"package": "com.example.plugin", "version": "1.2.3", "url": "https://example.com/p.tgz", "sha256": "' + "a" * 64 + '"}'

    def test_a_well_formed_manifest_passes(self):
        self.assertEqual(repo_checks.check_deps_manifest("m.json", self.MANIFEST), [])

    def test_a_manifest_needs_https_and_hashes(self):
        bad = '{"package": "p", "version": "1", "url": "http://example.com/p.tgz", "sha256": "ABC", "models": [{"file": "m.bytes"}]}'
        errors = repo_checks.check_deps_manifest("m.json", bad)
        self.assertIn("m.json: url must be https", errors)
        self.assertIn("m.json: sha256 must be 64 lowercase hex digits", errors)
        self.assertIn("m.json: every model needs a file and a sha256", errors)

    def test_the_sbom_records_the_pinned_download(self):
        component = self.component("unity-package:com.example.plugin", "1.2.3")
        component["hashes"] = [{"alg": "SHA-256", "content": "a" * 64}]
        component["externalReferences"] = [{"type": "distribution", "url": "https://example.com/p.tgz"}]
        self.assertEqual(repo_checks.check_pinned_sbom(self.sbom(component), [self.MANIFEST]), [])

        component["hashes"] = [{"alg": "SHA-256", "content": "b" * 64}]
        component["externalReferences"] = []
        errors = repo_checks.check_pinned_sbom(self.sbom(component), [self.MANIFEST])
        self.assertIn("SBOM: unity-package:com.example.plugin SHA-256 differs from tools/deps", errors)
        self.assertIn("SBOM: unity-package:com.example.plugin distribution URL differs from tools/deps", errors)

    def test_nested_components_need_a_license_and_scope(self):
        parent = self.component("unity-package:com.unity.ugui", "2.6.0")
        parent["components"] = [{"bom-ref": "inner", "name": "inner", "version": "1"}]
        errors = repo_checks.check_sbom(self.sbom(parent), {})
        self.assertIn("SBOM: inner has no license", errors)
        self.assertIn("SBOM: inner needs a scope (required, optional, or excluded)", errors)


class DocPairTests(unittest.TestCase):
    def test_pairs_pass_and_assets_are_ignored(self):
        tracked = ["docs/ja/ui/a.md", "docs/en/ui/a.md", "docs/assets/ui/styleboard.html"]
        self.assertEqual(repo_checks.check_doc_pairs(tracked), [])

    def test_missing_translation(self):
        tracked = ["docs/ja/ui/a.md", "docs/ja/ui/b.md", "docs/en/ui/a.md"]
        self.assertEqual(repo_checks.check_doc_pairs(tracked), ["Missing docs/en/ui/b.md"])


if __name__ == "__main__":
    unittest.main()
