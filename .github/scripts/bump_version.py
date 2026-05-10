#!/usr/bin/env python3
"""
bump_version.py — Bump versions.props semantic version (YYYY.Major.Minor.Patch).

Usage:
    python bump_version.py --type <major|minor|patch>

Rules:
    major  → Major++, Minor=0, Patch=0
    minor  → Minor++, Patch=0
    patch  → Patch++

Year is always checked and updated to the current UTC year with no cascade.
"""

import argparse
import os
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

VERSIONS_PROPS = Path(__file__).resolve().parents[2] / "versions.props"


def parse_version(path: Path) -> dict:
    tree = ET.parse(path)
    root = tree.getroot()
    props = root.find("PropertyGroup")
    if props is None:
        print("::error::versions.props has no PropertyGroup element.", file=sys.stderr)
        sys.exit(1)

    def get(name: str, default: str = "0") -> str:
        el = props.find(name)
        return el.text.strip() if el is not None and el.text else default

    return {
        "tree": tree,
        "root": root,
        "props": props,
        "VersionYear": get("VersionYear", str(datetime.now(timezone.utc).year)),
        "VersionMajor": get("VersionMajor", "0"),
        "VersionMinor": get("VersionMinor", "0"),
        "VersionPatch": get("VersionPatch", "0"),
    }


def write_version(state: dict, path: Path) -> None:
    props = state["props"]
    tree = state["tree"]

    for key in ("VersionYear", "VersionMajor", "VersionMinor", "VersionPatch"):
        el = props.find(key)
        if el is not None:
            el.text = state[key]

    # Preserve existing formatting style as best as possible
    ET.indent(tree, space="  ")
    tree.write(path, encoding="unicode", xml_declaration=False)

    # Ensure file ends with a newline
    content = path.read_text(encoding="utf-8")
    if not content.endswith("\n"):
        with path.open("a", encoding="utf-8") as stream:
            stream.write("\n")


def main() -> None:
    parser = argparse.ArgumentParser(description="Bump versions.props version segment.")
    parser.add_argument(
        "--type",
        choices=["major", "minor", "patch"],
        required=True,
        dest="bump_type",
        help="Which segment to bump (major|minor|patch).",
    )
    args = parser.parse_args()

    state = parse_version(VERSIONS_PROPS)

    year = int(state["VersionYear"])
    major = int(state["VersionMajor"])
    minor = int(state["VersionMinor"])
    patch = int(state["VersionPatch"])

    # Always update year if it has rolled over (no cascade)
    current_year = datetime.now(timezone.utc).year
    if current_year != year:
        print(f"Year rolled over: {year} → {current_year} (no cascade)")
        year = current_year

    bump = args.bump_type
    if bump == "major":
        major += 1
        minor = 0
        patch = 0
    elif bump == "minor":
        minor += 1
        patch = 0
    elif bump == "patch":
        patch += 1

    state["VersionYear"] = str(year)
    state["VersionMajor"] = str(major)
    state["VersionMinor"] = str(minor)
    state["VersionPatch"] = str(patch)

    write_version(state, VERSIONS_PROPS)

    new_version = f"{year}.{major}.{minor}.{patch}"
    print(f"Version bumped ({bump}): {new_version}")

    # Export to GITHUB_OUTPUT if available
    github_output = os.environ.get("GITHUB_OUTPUT")
    if github_output:
        with open(github_output, "a") as f:
            f.write(f"version={new_version}\n")


if __name__ == "__main__":
    main()
