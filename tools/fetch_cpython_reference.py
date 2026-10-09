"""Download selected official CPython source files at resolved, immutable commits."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from urllib.request import Request, urlopen

FILES = (
    "Grammar/python.gram", "Parser/Python.asdl", "Lib/opcode.py",
    "Python/bytecodes.c", "Python/compile.c", "Python/flowgraph.c",
    "Python/assemble.c", "Python/specialize.c", "Objects/abstract.c",
    "Include/internal/pycore_frame.h", "Objects/longobject.c", "Objects/typeobject.c",
    "Objects/classobject.c", "Python/ceval.c",
)


def fetch(url):
    with urlopen(Request(url, headers={"User-Agent": "SharpPy-reference-audit"}), timeout=60) as response:
        return response.read()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tags", nargs="+", default=["v3.12.0", "v3.12.15"])
    parser.add_argument("--output", type=Path, default=Path(".artifacts/cpython"))
    args = parser.parse_args()
    manifest = dict(downloaded_utc=datetime.now(timezone.utc).isoformat(), references=[])
    args.output.mkdir(parents=True, exist_ok=True)
    for tag in args.tags:
        if not tag.startswith("v3.12.") or not tag[6:].isdigit():
            parser.error("Only fixed v3.12.<patch> tags are accepted")
        commit_url = f"https://api.github.com/repos/python/cpython/commits/{tag}"
        commit = json.loads(fetch(commit_url))["sha"]
        reference = dict(tag=tag, commit=commit, resolution_url=commit_url, files=[])
        for name in FILES:
            url = f"https://raw.githubusercontent.com/python/cpython/{commit}/{name}"
            content = fetch(url)
            target = args.output / tag / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(content)
            reference["files"].append(dict(path=name, url=url, sha256=hashlib.sha256(content).hexdigest()))
        manifest["references"].append(reference)
        (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
        print(f"{tag}: {commit} ({len(FILES)} files)", flush=True)


if __name__ == "__main__":
    main()
