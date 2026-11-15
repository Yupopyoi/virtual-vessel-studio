"""Builds Voice Lab's download list, services/voice-lab/voice-components.json (Voice Lab detailed design 6.2), from
tools/voice/voice-components.sources.json and the files as they are on the developer's PC, so every size and SHA-256
is what was tested. With --release, it also copies the files to publish to the project's GitHub Release into one
folder, named as the list expects: only the project's own files, as no copies of others' files are kept (the user's
decision, 2026-11-11).

python -I tools/voice/voice_components.py --download <folder with uv.zip and rvc.zip> --rvc <RVC checkout>
    --voxcpm2 <VoxCPM2 snapshot> --sentis <folder with the templates and shared models> [--release <folder>]
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCES = os.path.join(ROOT, "tools", "voice", "voice-components.sources.json")
OUTPUT = os.path.join(ROOT, "services", "voice-lab", "voice-components.json")
SCHEMA_VERSION = 1


def sha256_of(path):
    digest = hashlib.sha256()
    with open(path, "rb") as file:
        for chunk in iter(lambda: file.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def expand(text, values):
    """The text with each {name} replaced by its value. Pure."""
    for name, value in values.items():
        text = text.replace("{" + name + "}", value)
    if "{" in text:
        raise ValueError(f"Unknown placeholder in '{text}'.")
    return text


def release_name(component):
    """The file's name in the GitHub Release, the last part of its URL. Pure."""
    return re.sub(r"\{[^}]*\}", "", component["url"]).rsplit("/", 1)[-1]


def build(sources, folders, hasher=sha256_of, size_of=os.path.getsize):
    """The download list from the sources and where their local copies are. Pure but for the two readers passed in."""
    values = dict(sources.get("bases", {}))
    values["release"] = sources["release"]
    locals_ = dict(folders)
    ids = set()
    components = []
    for component in sources["components"]:
        if component["id"] in ids:
            raise ValueError(f"Two components are named {component['id']}.")
        ids.add(component["id"])
        local = expand(component["local"], locals_)
        entry = {
            "id": component["id"],
            "url": expand(component["url"], values),
            "sha256": hasher(local),
            "size": size_of(local),
            "license": component["license"],
            "place": component["place"],
            "target": component["target"],
        }
        for key in ("extract", "strip"):
            if key in component:
                entry[key] = component[key]
        components.append(entry)
    return {
        "schemaVersion": SCHEMA_VERSION,
        "version": sources["version"],
        "python": sources["python"],
        "lock": sources["lock"],
        "components": components,
    }


def release_files(sources, folders):
    """(local path, name in the Release) for every file the Release holds. Pure."""
    for component in sources["components"]:
        if component["url"].startswith("{release}"):
            yield expand(component["local"], dict(folders)), release_name(component)


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--download", required=True)
    parser.add_argument("--rvc", required=True)
    parser.add_argument("--voxcpm2", required=True)
    parser.add_argument("--sentis", required=True)
    parser.add_argument("--release")
    args = parser.parse_args(argv)
    folders = {"download": args.download, "rvc": args.rvc, "voxcpm2-local": args.voxcpm2, "sentis": args.sentis}
    with open(SOURCES, encoding="utf-8") as file:
        sources = json.load(file)
    listed = build(sources, folders)
    with open(OUTPUT, "w", encoding="utf-8", newline="\n") as file:
        json.dump(listed, file, indent=2, ensure_ascii=False)
        file.write("\n")
    total = sum(c["size"] for c in listed["components"])
    print(f"Wrote {OUTPUT}: {len(listed['components'])} components, {total / 1024 ** 3:.2f} GB.")
    if args.release:
        os.makedirs(args.release, exist_ok=True)
        for local, name in release_files(sources, folders):
            shutil.copyfile(local, os.path.join(args.release, name))
        print(f"Copied the Release's files to {args.release}.")


if __name__ == "__main__":
    main(sys.argv[1:])
