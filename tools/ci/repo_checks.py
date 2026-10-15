"""Repository checks that need no Unity: run on every pull request (development workflow 18.1).

Checks the files tracked by git:

* Unity .meta files: every asset and folder under Assets/ has one, every .meta has its asset,
  and no two .meta files share a GUID (a common result of copying or merging assets).
* String tables: every locale file parses and has the same keys as the others.
* Documentation: docs/ja and docs/en contain the same documents.

Run from the repository root:  python tools/ci/repo_checks.py
Standard library only; Python 3.10 or later.
"""

from __future__ import annotations

import json
import posixpath
import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path
from typing import Iterable, Mapping

ASSETS_ROOT = "unity/VirtualVesselStudio/Assets"
STRING_TABLE_DIRS = ("unity/VirtualVesselStudio/Assets/VirtualVessel/UI/Strings",)
DOC_LANGUAGES = ("ja", "en")

_GUID = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)


def is_ignored_by_unity(path: str) -> bool:
    """Unity skips hidden names and names ending with '~'; they need no .meta file."""
    return any(part.startswith(".") or part.endswith("~") for part in path.split("/"))


def check_meta_files(tracked: Iterable[str], root: str = ASSETS_ROOT) -> list[str]:
    """Missing and orphaned .meta files under root, given the tracked paths (POSIX, repository-relative)."""
    prefix = root.rstrip("/") + "/"
    paths = {p for p in tracked if p.startswith(prefix) and not is_ignored_by_unity(p[len(prefix):])}
    metas = {p for p in paths if p.endswith(".meta")}
    assets = paths - metas

    folders: set[str] = set()
    for asset in assets:
        parent = posixpath.dirname(asset)
        while parent.startswith(prefix):
            folders.add(parent)
            parent = posixpath.dirname(parent)

    errors = []
    for item in sorted(assets | folders):
        if item + ".meta" not in metas:
            errors.append(f"Missing .meta: {item}")

    for meta in sorted(metas):
        target = meta[: -len(".meta")]
        if target not in assets and target not in folders:
            errors.append(f"Orphaned .meta (no asset or folder): {meta}")

    return errors


def check_duplicate_guids(meta_contents: Mapping[str, str]) -> list[str]:
    """.meta files that share a GUID, given each .meta path and its text."""
    owners: dict[str, list[str]] = defaultdict(list)
    for path, text in meta_contents.items():
        match = _GUID.search(text)
        if match:
            owners[match.group(1).lower()].append(path)

    return [
        f"Duplicate GUID {guid}: {', '.join(sorted(paths))}"
        for guid, paths in sorted(owners.items())
        if len(paths) > 1
    ]


def check_string_tables(tables: Mapping[str, str]) -> list[str]:
    """Parse errors and key differences between the locale files of one string table folder."""
    errors = []
    keys: dict[str, set[str]] = {}
    for path, text in sorted(tables.items()):
        try:
            data = json.loads(text)
        except json.JSONDecodeError as error:
            errors.append(f"Invalid JSON in {path}: {error}")
            continue

        strings = data.get("strings") if isinstance(data, dict) else None
        if not isinstance(strings, dict):
            errors.append(f"No \"strings\" object in {path}")
            continue

        keys[path] = set(strings)

    everything = set().union(*keys.values()) if keys else set()
    for path, own in sorted(keys.items()):
        for key in sorted(everything - own):
            errors.append(f"Missing key in {path}: {key}")

    return errors


def check_doc_pairs(tracked: Iterable[str], languages: tuple[str, ...] = DOC_LANGUAGES) -> list[str]:
    """Documents that exist under one docs/<language> folder but not the others."""
    by_language = {
        language: {p[len(f"docs/{language}/"):] for p in tracked if p.startswith(f"docs/{language}/")}
        for language in languages
    }
    everything = set().union(*by_language.values())
    return [
        f"Missing docs/{language}/{document}"
        for language in languages
        for document in sorted(everything - by_language[language])
    ]


def tracked_files(repository: Path) -> list[str]:
    output = subprocess.run(
        ["git", "ls-files", "-z"], cwd=repository, check=True, capture_output=True
    ).stdout
    return [p for p in output.decode("utf-8").split("\0") if p]


def run(repository: Path) -> list[str]:
    tracked = tracked_files(repository)
    errors = check_meta_files(tracked)

    metas = {p: (repository / p).read_text(encoding="utf-8", errors="replace") for p in tracked if p.endswith(".meta")}
    errors += check_duplicate_guids(metas)

    for folder in STRING_TABLE_DIRS:
        tables = {
            p: (repository / p).read_text(encoding="utf-8")
            for p in tracked
            if posixpath.dirname(p) == folder and p.endswith(".json")
        }
        if not tables:
            errors.append(f"No string tables found in {folder}")
        errors += check_string_tables(tables)

    errors += check_doc_pairs(tracked)
    return errors


def main() -> int:
    repository = Path(__file__).resolve().parents[2]
    errors = run(repository)
    for error in errors:
        print(error)

    if errors:
        print(f"\n{len(errors)} problem(s) found.")
        return 1

    print("Repository checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
