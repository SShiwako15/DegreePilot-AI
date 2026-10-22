"""Offline, high-confidence secret/privacy checks; never print matched values."""

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PATTERNS = (
    re.compile(rb"sk-(?:proj-|svcacct-)[A-Za-z0-9_-]{40,}"),
    re.compile(rb"sk-[A-Za-z0-9]{48}"),
    re.compile(rb"gh[pousr]_[A-Za-z0-9]{36,}"),
    re.compile(rb"AKIA[A-Z0-9]{16}"),
    re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
)


def main() -> int:
    names = (
        subprocess.check_output(
            ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=ROOT
        )
        .decode()
        .split("\0")
    )
    failures: set[str] = set()
    scanned = 0
    for name in names:
        if not name:
            continue
        path = ROOT / name
        if path.name == ".env" or (path.name.startswith(".env.") and path.name != ".env.example"):
            failures.add(name)
        if not path.is_file() or path.suffix.lower() in {".png", ".jpg", ".jpeg", ".ico", ".woff2"}:
            continue
        data = path.read_bytes()
        scanned += 1
        if any(pattern.search(data) for pattern in PATTERNS):
            failures.add(name)
    for path in (ROOT / "apps/web/dist").rglob("*"):
        if path.is_file() and path.suffix in {".js", ".html", ".css"}:
            if any(pattern.search(path.read_bytes()) for pattern in PATTERNS):
                failures.add(str(path.relative_to(ROOT)))
    if failures:
        print("Potential secret or private environment file: " + ", ".join(sorted(failures)))
        return 1
    print(
        f"Privacy scan passed: {scanned} source files plus frontend bundle; no high-confidence key patterns."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
