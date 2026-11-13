#!/usr/bin/env python3
"""Check real gc binaries on Linux, Windows and macOS without clipboard access."""

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


binary = Path(sys.argv[1]).resolve()
environment = dict(os.environ, NO_COLOR="1")
output_directories = []


def run(cwd, *args):
    result = subprocess.run([str(binary), *map(str, args)], cwd=cwd, env=environment,
                            capture_output=True, encoding="utf-8", errors="strict", timeout=30)
    if result.returncode:
        raise AssertionError((result.returncode, result.stdout, result.stderr))
    return result.stdout


def exported(cwd, *args):
    output = run(cwd, *args)
    match = re.search(r"^Folder: (.+)$", output, re.MULTILINE)
    assert match, output
    directory = Path(match.group(1).strip())
    output_directories.append(directory)
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    restored = bytearray()
    for part in manifest["parts"]:
        data = (directory / part["file"]).read_bytes()
        assert len(data) == part["bytes"] <= 50000
        data.decode("utf-8")
        assert hashlib.sha256(data).hexdigest() == part["sha256"]
        offset, count = part["payload_offset"], part["payload_bytes"]
        restored.extend(data[offset:offset + count])
    assert hashlib.sha256(restored).hexdigest() == manifest["source_sha256"]
    assert len(restored) == manifest["source_bytes"]
    return bytes(restored), manifest


with tempfile.TemporaryDirectory(prefix="gc context smoke ") as name:
    root = Path(name)
    environment["XDG_CONFIG_HOME"] = str(root / "config")
    environment["APPDATA"] = str(root / "appdata")
    repo = root / "project with spaces"
    repo.mkdir()
    subprocess.run(["git", "init", "--quiet", str(repo)], check=True)
    files = {
        "src/Example.cs": "class SourceMarker { string Value = \"" + "বাংলা🦊" * 12000 + "\"; }\r\n",
        "Runtime/Runner.cs": "class RuntimeMarker {}\n",
        "Editor/Inspector.cs": "class EditorMarker {}\n",
        "Tests/Editor/Skip.cs": "class NeverIncludeTestMarker {}\n",
        "Samples~/Skip.cs": "class NeverIncludeSampleMarker {}\n",
        "Library/Skip.cs": "class NeverIncludeCacheMarker {}\n",
    }
    for relative, content in files.items():
        target = repo / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(content.encode("utf-8"))
    baseline = root / "baseline.md"
    run(root, "-u", "-e", "cs", "--output", baseline, repo)
    restored, manifest = exported(root, repo, "-u", "-e", "cs", "-k")
    assert restored == baseline.read_bytes()
    assert len(manifest["parts"]) > 1
    assert b"SourceMarker" in restored and b"RuntimeMarker" in restored and b"EditorMarker" in restored
    assert b"NeverInclude" not in restored

    selected, _ = exported(root, "-e", "cs", repo / "src", "-k50")
    assert b"SourceMarker" in selected and b"RuntimeMarker" not in selected
    single, _ = exported(root, repo / "Runtime/Runner.cs", "-k")
    assert b"RuntimeMarker" in single and b"SourceMarker" not in single

    run(repo, "init", "-u", "-k", "--temp", "-e", "cs")
    remembered, _ = exported(repo)
    assert b"SourceMarker" in remembered and b"RuntimeMarker" in remembered
    remembered_src, _ = exported(repo, "src")
    assert b"SourceMarker" in remembered_src and b"RuntimeMarker" not in remembered_src

    dependency = repo / "dependency"
    dependency.mkdir()
    subprocess.run(["git", "init", "--quiet", str(dependency)], check=True)
    (dependency / "src").mkdir()
    (dependency / "src/Child.cs").write_text("class RegisteredSubmoduleMarker {}", encoding="utf-8")
    (repo / ".gitmodules").write_text('[submodule "dependency"]\n\tpath = dependency\n', encoding="utf-8")
    nested, _ = exported(repo)
    assert b"RegisteredSubmoduleMarker" in nested

    bad = subprocess.run([str(binary), "-e", "cs", "--splti", "50KB"], cwd=repo, env=environment,
                         capture_output=True, text=True, timeout=30)
    assert bad.returncode != 0
    print("PASS: direct folders/files, saved gc defaults, registered submodules, source exclusions, UTF-8 byte caps and exact reconstruction.")

for directory in output_directories:
    shutil.rmtree(directory)
