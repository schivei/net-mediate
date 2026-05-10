#!/usr/bin/env python3
"""Generate a coverage badge from Cobertura reports."""

from __future__ import annotations

import html
import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
COVERAGE_DIR = Path(os.environ.get("COVERAGE_DIR", ROOT / "coverage"))
BADGE_DIR = Path(os.environ.get("COVERAGE_BADGE_DIR", ROOT / "docs" / "badges"))


def format_rate(rate: float) -> str:
    return f"{rate:.1f}%"


def badge_color(rate: float) -> str:
    if rate >= 90:
        return "#28a745"
    if rate >= 80:
        return "#97CA00"
    if rate >= 70:
        return "#a4a61d"
    if rate >= 60:
        return "#dfb317"
    return "#e05d44"


def text_width(text: str) -> int:
    return max(40, 7 * len(text) + 10)


def text_length(text: str) -> int:
    return text_width(text) * 10


def parse_condition_coverage(coverage: str, hits: int) -> tuple[int, int]:
    if "(" in coverage and "/" in coverage:
        try:
            fraction = coverage.split("(", 1)[1].split(")", 1)[0]
            covered_raw, valid_raw = fraction.split("/", 1)
            covered = int(covered_raw)
            valid = int(valid_raw)
            if valid > 0 and covered >= 0:
                return covered, valid
        except (IndexError, ValueError):
            pass

    covered = 1 if hits > 0 else 0
    return covered, 1


def merge_line_coverage(
    merged_lines: dict[tuple[str, int], int],
    line_number: int,
    hits: int,
    filename: str,
) -> None:
    line_key = (filename, line_number)
    merged_lines[line_key] = max(merged_lines.get(line_key, 0), hits)


def merge_branch_coverage(
    merged_branches: dict[tuple[str, int], tuple[int, int]],
    line: ET.Element,
    line_number: int,
    hits: int,
    filename: str,
) -> None:
    if line.get("branch") != "True":
        return

    covered, valid = parse_condition_coverage(
        line.get("condition-coverage", ""),
        hits,
    )

    branch_key = (filename, line_number)
    previous = merged_branches.get(branch_key, (0, 0))
    merged_branches[branch_key] = (
        max(previous[0], covered),
        max(previous[1], valid),
    )


def merge_class_coverage(
    merged_lines: dict[tuple[str, int], int],
    merged_branches: dict[tuple[str, int], tuple[int, int]],
    cls: ET.Element,
) -> None:
    filename = cls.get("filename")
    if not filename or "/obj/" in filename.replace("\\", "/"):
        return

    for line in cls.findall("./lines/line"):
        line_number = int(line.get("number", 0) or 0)
        hits = int(line.get("hits", 0) or 0)

        merge_line_coverage(merged_lines, line_number, hits, filename)
        merge_branch_coverage(merged_branches, line, line_number, hits, filename)


def load_rates() -> tuple[float, float]:
    xml_files = sorted(COVERAGE_DIR.rglob("coverage.cobertura.xml"))
    if not xml_files:
        raise FileNotFoundError(f"No coverage.cobertura.xml files found under {COVERAGE_DIR}")

    merged_lines: dict[tuple[str, int], int] = {}
    merged_branches: dict[tuple[str, int], tuple[int, int]] = {}

    for xml_file in xml_files:
        root = ET.parse(xml_file).getroot()
        for cls in root.findall(".//class"):
            merge_class_coverage(merged_lines, merged_branches, cls)

    total_valid = len(merged_lines)
    total_covered = sum(1 for hits in merged_lines.values() if hits > 0)
    total_branches_valid = sum(valid for _, valid in merged_branches.values())
    total_branches_covered = sum(covered for covered, _ in merged_branches.values())

    line_rate = (total_covered / total_valid * 100) if total_valid > 0 else 0.0
    branch_rate = (
        total_branches_covered / total_branches_valid * 100
        if total_branches_valid > 0 else 0.0
    )
    return line_rate, branch_rate


def build_svg(label: str, value: str, title: str, color: str) -> str:
    label_width = text_width(label)
    value_width = text_width(value)
    label_length = text_length(label)
    value_length = text_length(value)
    total_width = label_width + value_width

    label_x = label_width / 2
    value_x = label_width + value_width / 2

    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="{total_width}" height="20" role="img" aria-label="{html.escape(title)}">
<title>{html.escape(title)}</title>
<linearGradient id="s" x2="0" y2="100%">
  <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
  <stop offset="1" stop-opacity=".1"/>
</linearGradient>
<clipPath id="r">
  <rect width="{total_width}" height="20" rx="3" fill="#fff"/>
</clipPath>
<g clip-path="url(#r)">
  <rect width="{label_width}" height="20" fill="#555"/>
  <rect x="{label_width}" width="{value_width}" height="20" fill="{color}"/>
  <rect width="{total_width}" height="20" fill="url(#s)"/>
</g>
<g fill="#fff" text-anchor="middle" font-family="Verdana,Geneva,DejaVu Sans,sans-serif" text-rendering="geometricPrecision" font-size="110">
  <text aria-hidden="true" x="{label_x * 10:.0f}" y="150" fill="#010101" fill-opacity=".3" transform="scale(.1)" textLength="{label_length}">{html.escape(label)}</text>
  <text x="{label_x * 10:.0f}" y="140" transform="scale(.1)" fill="#fff" textLength="{label_length}">{html.escape(label)}</text>
  <text aria-hidden="true" x="{value_x * 10:.0f}" y="150" fill="#010101" fill-opacity=".3" transform="scale(.1)" textLength="{value_length}">{html.escape(value)}</text>
  <text x="{value_x * 10:.0f}" y="140" transform="scale(.1)" fill="#fff" textLength="{value_length}">{html.escape(value)}</text>
</g>
</svg>
"""


def build_badges(line_rate: float, branch_rate: float) -> dict[str, str]:
    return {
        "coverage.svg": build_svg(
            label="coverage",
            value=f"{format_rate(line_rate)} lines / {format_rate(branch_rate)} branches",
            title=f"coverage: {format_rate(line_rate)} lines, {format_rate(branch_rate)} branches",
            color=badge_color(min(line_rate, branch_rate)),
        ),
        "coverage-lines.svg": build_svg(
            label="coverage lines",
            value=format_rate(line_rate),
            title=f"coverage lines: {format_rate(line_rate)}",
            color=badge_color(line_rate),
        ),
        "coverage-branches.svg": build_svg(
            label="coverage branches",
            value=format_rate(branch_rate),
            title=f"coverage branches: {format_rate(branch_rate)}",
            color=badge_color(branch_rate),
        ),
    }


def main() -> int:
    try:
        line_rate, branch_rate = load_rates()
    except FileNotFoundError as exc:
        print(str(exc), file=sys.stderr)
        return 1

    BADGE_DIR.mkdir(parents=True, exist_ok=True)
    badges = build_badges(line_rate, branch_rate)
    for filename, svg in badges.items():
        output_path = BADGE_DIR / filename
        output_path.write_text(svg, encoding="utf-8")
        print(f"Wrote {output_path}")
    print(f"Coverage rates: {format_rate(line_rate)} lines / {format_rate(branch_rate)} branches.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
