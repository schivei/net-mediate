#!/usr/bin/env python3
"""
analyze_version_impact.py — Determine whether a PR warrants a major or minor bump.

Usage:
    python analyze_version_impact.py <base_sha> <head_sha>

Algorithm:
    1. Run `git diff --numstat <base_sha>..<head_sha> -- src/` to get per-file
       added/deleted line counts (scoped to src/ only).
    2. Count total source lines on the base ref for all files under src/.
    3. Compute impact ratio: (added + deleted) / (base_total + added)
    4. If ratio >= 0.20 (20%) → print "major"; otherwise → print "minor".

Output:
    Prints exactly "major" or "minor" to stdout (plus a GITHUB_OUTPUT entry).
"""

import subprocess
import sys
import os
from pathlib import Path

THRESHOLD = 0.20  # 20 % impact triggers a major bump


def git(*args: str, cwd: Path | None = None, required: bool = True) -> str:
    result = subprocess.run(
        ["git", *args],
        capture_output=True,
        text=True,
        cwd=cwd or Path(__file__).resolve().parents[2],
    )
    if result.returncode != 0:
        msg = f"git {' '.join(args)} failed: {result.stderr.strip()}"
        if required:
            print(f"::error::{msg}", file=sys.stderr)
            sys.exit(1)
        else:
            print(f"::warning::{msg}", file=sys.stderr)
    return result.stdout


def count_lines_at_ref(base_sha: str) -> int:
    """Count total lines in src/**/*.cs at the base commit."""
    # List all tree entries under src/ at base_sha
    ls = git("ls-tree", "-r", "--name-only", base_sha, "src/")
    total = 0
    for path in ls.splitlines():
        if path.endswith(".cs"):
            content = git("show", f"{base_sha}:{path}")
            total += content.count("\n") + (1 if content and not content.endswith("\n") else 0)
    return total


def parse_numstat(base_sha: str, head_sha: str) -> tuple[int, int]:
    """Return (total_added, total_deleted) for src/ between base and head."""
    output = git("diff", "--numstat", f"{base_sha}..{head_sha}", "--", "src/")
    added = 0
    deleted = 0
    for line in output.splitlines():
        parts = line.split("\t")
        if len(parts) < 3:
            continue
        try:
            a = int(parts[0])
            d = int(parts[1])
        except ValueError:
            # Binary files show '-' — treat as 0
            a, d = 0, 0
        added += a
        deleted += d
    return added, deleted


def main() -> None:
    if len(sys.argv) != 3:
        print(f"Usage: {sys.argv[0]} <base_sha> <head_sha>", file=sys.stderr)
        sys.exit(1)

    base_sha, head_sha = sys.argv[1], sys.argv[2]

    base_total = count_lines_at_ref(base_sha)
    added, deleted = parse_numstat(base_sha, head_sha)

    denominator = base_total + added
    if denominator == 0:
        # No source at all — treat as minor
        ratio = 0.0
    else:
        ratio = (added + deleted) / denominator

    print(
        f"Impact analysis: base_lines={base_total}, added={added}, deleted={deleted}, "
        f"ratio={ratio:.2%}, threshold={THRESHOLD:.0%}",
        file=sys.stderr,
    )

    bump_type = "major" if ratio >= THRESHOLD else "minor"
    print(bump_type)

    # Export to GITHUB_OUTPUT if available
    github_output = os.environ.get("GITHUB_OUTPUT")
    if github_output:
        with open(github_output, "a") as f:
            f.write(f"bump_type={bump_type}\n")


if __name__ == "__main__":
    main()
