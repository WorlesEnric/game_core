#!/usr/bin/env python3
"""Create any missing Unity `.meta` file under the given roots, with fresh unique GUIDs.

Usage: python3 tools/make_unity_metas.py [root ...]

Defaults to the whole repository. Every `.cs`, `.asmdef`, `.json`, `.preset`, `.shader`, `.asset` and directory
below a root that is not already covered by a `.meta` gets one, in the exact byte shape this repository uses:

  file      fileFormatVersion: 2\n guid: <32 lowercase hex>\n        (no trailing newline)
  directory fileFormatVersion: 2\n guid: <32 lowercase hex>\n folderAsset: yes\n DefaultImporter:\n   externalObjects: {}\n   userData: \n   assetBundleName: \n   assetBundleVariant: \n        (no trailing newline)

Existing `.meta` files are never touched, and GUIDs already present anywhere in the tree are never reused. The
script is idempotent: a second run reports zero creations.
"""

import os
import re
import sys
import uuid

SKIP_DIRS = {"bin", "obj", ".git", "Library", "Temp", "Logs", "obj~", ".vs", ".idea", "Artifacts~"}
ASSET_SUFFIXES = (".cs", ".asmdef", ".json", ".asmref", ".shader", ".asset", ".preset", ".uxml", ".uss")

GUID_PATTERN = re.compile(r"^guid: ([0-9a-f]{32})$", re.MULTILINE)
FILE_TEXT = "fileFormatVersion: 2\nguid: {guid}\n"

# Unity records a typed importer for some asset kinds. Matching the shape the repository already uses keeps an
# import from rewriting these files (and therefore keeps the GUIDs in them stable).
IMPORTER_BLOCKS = {
    ".asmdef": "AssemblyDefinitionImporter",
    ".asmref": "AssemblyDefinitionReferenceImporter",
    ".json": "PackageManifestImporter",
}

FOLDER_TEXT = (
    "fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n"
    "  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
)

IMPORTER_TEXT = (
    "fileFormatVersion: 2\nguid: {guid}\n{importer}:\n"
    "  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
)


def existing_guids(roots):
    guids = set()
    for root in roots:
        for base, dirs, files in os.walk(root):
            dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
            for name in files:
                if not name.endswith(".meta"):
                    continue
                try:
                    with open(os.path.join(base, name), "r", encoding="utf-8") as handle:
                        text = handle.read()
                except OSError:
                    continue
                match = GUID_PATTERN.search(text)
                if match:
                    guids.add(match.group(1))
    return guids


def new_guid(used):
    while True:
        candidate = uuid.uuid4().hex
        if candidate not in used:
            used.add(candidate)
            return candidate


def write_meta(path, text):
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


def scan(roots):
    used = existing_guids(roots)
    created = []
    for root in roots:
        for base, dirs, files in os.walk(root):
            dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS)
            for name in sorted(dirs):
                directory = os.path.join(base, name)
                meta = directory + ".meta"
                if not os.path.exists(meta):
                    write_meta(meta, FOLDER_TEXT.format(guid=new_guid(used)))
                    created.append(meta)
            for name in sorted(files):
                if not name.endswith(ASSET_SUFFIXES) or name.endswith(".meta"):
                    continue
                path = os.path.join(base, name)
                meta = path + ".meta"
                if not os.path.exists(meta):
                    importer = IMPORTER_BLOCKS.get(os.path.splitext(name)[1])
                    guid = new_guid(used)
                    text = FILE_TEXT if importer is None else IMPORTER_TEXT
                    write_meta(meta, text.format(guid=guid, importer=importer))
                    created.append(meta)
    return created


def main(argv):
    roots = argv[1:] or ["."]
    for root in roots:
        if not os.path.isdir(root):
            print("not a directory: " + root, file=sys.stderr)
            return 2
    created = scan(roots)
    for path in created:
        print("created " + path)
    print("created {0} meta file(s)".format(len(created)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
