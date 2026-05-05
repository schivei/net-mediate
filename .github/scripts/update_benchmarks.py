#!/usr/bin/env python3
"""
Update docs/BENCHMARKS.md (and the Docusaurus website mirror) with the
latest BenchmarkDotNet results.

Called by CI after running the benchmarks.  Reads three files:
  $BENCH_REPORT      – BenchmarkDotNet GitHub markdown report for the PR branch
  $BENCH_BASE_REPORT – BenchmarkDotNet GitHub markdown report for the base branch
                       (same-run baseline; produced by running the base branch
                       benchmark in the same CI job via git worktree).
                       When present, this is preferred over the stored HTML-comment
                       baseline for timing comparison.
  $BENCH_BASE        – BENCHMARKS.md from the target branch (fallback baseline)
  docs/BENCHMARKS.md – the file to be updated (in the current workspace)

Baseline storage
----------------
The baseline is stored as a JSON **array** (ring buffer) of up to three entries,
newest first.  The format is:

  <!-- netmediate-bench-baseline: [{"cmd":67.2,"notify":100.8,...,"cmd_a":48,"notify_a":288,...},
                                   {...}, {...}] -->

Only main-branch pushes (BENCH_BASELINE_ONLY=true) may update the ring.  PR runs
read the ring (using the **median** of up to three entries for each metric) but
never write to it, preventing contamination from in-flight PR branches.

On the very first push to an empty ring the new measurement is stored three times
so that a stable median is available immediately.

Old single-dict format is migrated automatically on first read.

Environment variables consumed:
  BENCH_REPORT              – path to PR BenchmarkDotNet markdown report
  BENCH_BASE_REPORT         – path to base-branch BenchmarkDotNet markdown report (optional)
  BENCH_BASE                – path to base-branch BENCHMARKS.md (fallback baseline)
  BRANCH                    – current PR head branch name
  COMMIT_SHA                – full SHA of the head commit
  BASE_REF                  – target branch name (for labelling the baseline column)
  BENCHMARKS_MD             – path to the doc to update (defaults to docs/BENCHMARKS.md)
  BENCHMARKS_MD_WEBSITE     – path to Docusaurus website mirror (defaults to
                               website/docs/performance/benchmarks.md)
  BENCH_BASELINE_ONLY       – when "true", only refresh the stored ring and exit
"""
import re
import json
import sys
import os
import statistics
from datetime import datetime, timezone

BRANCH              = os.environ.get('BRANCH', 'unknown')
COMMIT              = os.environ.get('COMMIT_SHA', 'unknown')[:7]
BASE_REF            = os.environ.get('BASE_REF', 'main')
DATE                = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M UTC')
DOC_PATH            = os.environ.get('BENCHMARKS_MD', 'docs/BENCHMARKS.md')
WEBSITE_DOC_PATH    = os.environ.get('BENCHMARKS_MD_WEBSITE',
                                     'website/docs/performance/benchmarks.md')
REPORT_PATH         = os.environ.get('BENCH_REPORT', 'bench-report.md')
BASE_PATH           = os.environ.get('BENCH_BASE', 'benchmarks-base.md')
BASE_REPORT_PATH    = os.environ.get('BENCH_BASE_REPORT', '')
# When BENCH_BASELINE_ONLY=true the script only pushes a new entry onto the
# stored ring buffer and syncs the website file — used on main-branch pushes.
# PR runs must NOT update the ring to prevent branch contamination.
BASELINE_ONLY       = os.environ.get('BENCH_BASELINE_ONLY', '').lower() in ('1', 'true', 'yes')

# ---------------------------------------------------------------------------
# Ring-buffer baseline helpers
# ---------------------------------------------------------------------------
# The baseline is stored as a JSON array of up to three dicts, newest first.
# Old single-dict format is accepted and migrated transparently.
_BL_PAT = re.compile(r'<!-- netmediate-bench-baseline: (.+?) -->')


def parse_baseline_ring(doc_text: str) -> list:
    """Return baseline ring as a list of dicts (newest first), or [] if absent."""
    m = _BL_PAT.search(doc_text)
    if not m:
        return []
    try:
        data = json.loads(m.group(1))
        if isinstance(data, dict):
            return [data]          # migrate old single-dict format
        if isinstance(data, list):
            return [e for e in data if isinstance(e, dict)]
    except json.JSONDecodeError:
        pass
    return []


def median_from_ring(ring: list, key: str):
    """Compute the median of *key* across all ring entries. Returns None if absent."""
    values = [entry[key] for entry in ring if key in entry]
    if not values:
        return None
    return statistics.median(values)


def ring_to_comment(ring: list) -> str:
    return '<!-- netmediate-bench-baseline: ' + json.dumps(ring, separators=(',', ':')) + ' -->'


# ---------------------------------------------------------------------------
# Read input files
# ---------------------------------------------------------------------------
with open(REPORT_PATH) as f:
    report = f.read()
with open(DOC_PATH) as f:
    doc = f.read()
# BASE_PATH is only needed for PR full-update mode.
if not BASELINE_ONLY:
    with open(BASE_PATH) as f:
        base_doc = f.read()
else:
    base_doc = ''

# ---------------------------------------------------------------------------
# Single source of truth: BenchmarkDotNet method description → (key, display label).
# Method names use double-space between words as emitted by the [Benchmark(Description=...)] attribute.
BENCHMARKS: dict[str, tuple[str, str]] = {
    'Command  Send':         ('cmd',     'Command `Send`'),
    'Notification  Notify':  ('notify',  'Notification `Notify`'),
    'Request  Request':      ('request', 'Request `Request`'),
    'Stream  RequestStream': ('stream',  'Stream `RequestStream`'),
}
ORDERED_KEYS = ['cmd', 'notify', 'request', 'stream']
KEY_TO_LABEL: dict[str, str] = {v[0]: v[1] for v in BENCHMARKS.values()}

# Parse Throughput-job rows from a BenchmarkDotNet GitHub markdown report.
# Column order: Method | Job | IterCount | LaunchCount | RunStrategy | WarmupCount
#               | Mean  | Error | StdDev | Gen0 | Allocated
# Only rows with RunStrategy="Throughput" (Job-XXXXXX jobs) are matched.
# Single quotes in method names are HTML-encoded as &#39; or &#x27;.
# Capture groups:
#   1 = Method description (between surrounding quotes)
#   2 = Mean (ns)   3 = Error (ns)   4 = Gen0   5 = Allocated
row_re = re.compile(
    # Method name wrapped in HTML-encoded or literal single quotes
    r"(?:&#39;|'|&#x27;)(.*?)(?:&#39;|'|&#x27;)"
    # Throughput-job row: Job column starts with "Job-", RunStrategy="Throughput"
    r"\s*\|\s*Job-\w+\s*\|[^|]*\|[^|]*\|\s*Throughput\s*\|"
    # WarmupCount (skip)  |  Mean (group 2, ns)  |  Error (group 3, ns)
    r"[^|]*\|\s*([\d.]+)\s*ns\s*\|\s*([\d.]+)\s*ns\s*\|"
    # StdDev (skip)  |  Gen0 (group 4)  |  Allocated (group 5)
    r"[^|]*\|\s*([\d.]+)\s*\|\s*([\d.]+\s*[BKM]*)\s*\|"
)


def parse_report_metrics(text: str) -> dict:
    """Return {key: {mean, error, gen0, alloc}} from a BenchmarkDotNet github report."""
    result = {}
    for m in row_re.finditer(text):
        method = m.group(1)
        for method_name, (key, _) in BENCHMARKS.items():
            if method_name in method:
                result[key] = {
                    'mean':  float(m.group(2)),
                    'error': float(m.group(3)),
                    'gen0':  float(m.group(4)),
                    'alloc': m.group(5).strip(),
                }
                break
    return result


metrics: dict[str, dict] = parse_report_metrics(report)

if not metrics:
    print(
        'Warning: no Throughput-job rows found in bench-report.md — skipping update.',
        file=sys.stderr,
    )
    sys.exit(0)

# ---------------------------------------------------------------------------
# Parse the live base-branch benchmark report (same-run baseline).
# When present, this provides a direct apples-to-apples comparison because
# both branches were measured on the same machine during the same CI job.
# Falls back to the HTML-comment stored baseline when not available.
# ---------------------------------------------------------------------------
live_base_metrics: dict[str, dict] = {}
if BASE_REPORT_PATH and os.path.isfile(BASE_REPORT_PATH):
    try:
        with open(BASE_REPORT_PATH) as f:
            live_base_metrics = parse_report_metrics(f.read())
        if live_base_metrics:
            print(f'Same-run base metrics loaded from {BASE_REPORT_PATH} '
                  f'({len(live_base_metrics)} benchmarks).')
        else:
            print(f'Warning: {BASE_REPORT_PATH} contained no parseable rows.',
                  file=sys.stderr)
    except Exception as exc:
        print(f'Warning: could not parse {BASE_REPORT_PATH}: {exc}', file=sys.stderr)

# ---------------------------------------------------------------------------
# Extract system info from the report's fenced ini block at the top
# ---------------------------------------------------------------------------
os_m    = re.search(r'BenchmarkDotNet v[\d.]+, (.+)', report)
cpu_m   = re.search(r'^([ \t]*[\w][ \t\w.()/,]*GHz[^\n]*)', report, re.MULTILINE)
sdk_m   = re.search(r'\.NET SDK ([\d.]+)', report)
host_m  = re.search(r'\[Host\]\s+: (.+)', report)
os_str   = os_m.group(1).strip()   if os_m   else 'unknown'
cpu_str  = cpu_m.group(1).strip()  if cpu_m  else 'unknown'
sdk_str  = sdk_m.group(1).strip()  if sdk_m  else 'unknown'
host_str = host_m.group(1).strip() if host_m else 'unknown'

# ---------------------------------------------------------------------------
# Read stored baseline ring from base-branch doc (HTML comment fallback).
# Used only when no live base-branch benchmark report is available.
# ---------------------------------------------------------------------------
baseline_ring: list = []
for candidate_doc in (base_doc, doc):
    ring = parse_baseline_ring(candidate_doc)
    if ring:
        baseline_ring = ring
        break

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
# Timing tolerance: ±10% accounts for natural CI hardware variance.
# Allocations (parse_alloc_bytes / compare_alloc_str) are fully deterministic
# and not affected by CPU load — use them as the primary regression signal.
THRESHOLD_PERCENT  = 10.0
ALLOC_THRESHOLD_B  = 8    # allocation delta of 8 bytes or less is measurement/rounding noise


def parse_alloc_bytes(alloc: str) -> float:
    """Convert a BenchmarkDotNet allocation string to bytes.

    Examples: '48 B' → 48.0, '1.23 KB' → 1259.52, '-' → 0.0
    """
    alloc = alloc.strip()
    if not alloc or alloc == '-':
        return 0.0
    m = re.match(r'^([\d.]+)\s*([BKMG]?)B?$', alloc, re.IGNORECASE)
    if not m:
        return 0.0
    val    = float(m.group(1))
    prefix = m.group(2).upper()
    # 'B' is consumed by the trailing `B?` in the regex, so group(2) is either
    # empty (plain bytes), or K/M/G for larger units.  No separate 'B' entry needed.
    mult   = {'': 1.0, 'K': 1024.0, 'M': 1048576.0, 'G': 1073741824.0}
    return val * mult.get(prefix, 1.0)


def get_base_mean(key: str):
    """Return baseline mean_ns — live same-run result preferred; falls back to ring median."""
    if key in live_base_metrics:
        return live_base_metrics[key]['mean']
    return median_from_ring(baseline_ring, key)


def get_base_alloc_bytes(key: str) -> float:
    """Return baseline alloc bytes — live same-run result preferred; falls back to ring median."""
    if key in live_base_metrics:
        return parse_alloc_bytes(live_base_metrics[key]['alloc'])
    val = median_from_ring(baseline_ring, f'{key}_a')
    return val if val is not None else 0.0


def throughput_str(ns: float) -> str:
    if ns <= 0:
        return 'N/A'
    t = 1e9 / ns
    return f'~{t/1e6:.1f}M msg/s' if t >= 1e6 else f'~{t/1e3:.0f}K msg/s'


def compare_str(key: str, new_ns: float) -> str:
    """Timing comparison (±10% tolerance for CI hardware variance)."""
    old_ns = get_base_mean(key)
    if old_ns is None or old_ns <= 0:
        return '—'
    d = (new_ns - old_ns) / old_ns * 100
    if abs(d) <= THRESHOLD_PERCENT:
        return f'≈ ({d:+.1f}%)'
    return f'✅ improved ({d:+.1f}%)' if d < 0 else f'⚠️ degraded ({d:+.1f}%)'


def compare_alloc_str(key: str, new_alloc_str: str) -> str:
    """Allocation comparison — deterministic, unaffected by CPU load."""
    new_b = parse_alloc_bytes(new_alloc_str)
    old_b = get_base_alloc_bytes(key)
    if old_b <= 0:
        return '—'
    delta = new_b - old_b
    if abs(delta) <= ALLOC_THRESHOLD_B:
        return '✅ same'
    icon = '⚠️' if delta > 0 else '✅'
    return f"{icon} {'+' if delta > 0 else ''}{delta:.0f} B"


# Whether timing comparison used the live same-run base or the stored baseline
_using_live_base = bool(live_base_metrics)

# ---------------------------------------------------------------------------
# Website sync helper
# ---------------------------------------------------------------------------
# The Docusaurus website version of BENCHMARKS.md mirrors the docs source but
# needs a frontmatter header and website-relative "See Also" links.
_WEBSITE_FRONTMATTER = '---\nsidebar_position: 1\n---\n\n'
_DOCS_SEE_ALSO = (
    '- [RESILIENCE.md](RESILIENCE.md) — resilience package guide\n'
    '- [AOT.md](AOT.md) — AOT/NativeAOT compatibility guide\n'
    '- [SOURCE_GENERATION.md](SOURCE_GENERATION.md) — source generator guide'
)
_WEBSITE_SEE_ALSO = (
    '- [Resilience](../advanced/resilience) — resilience package guide\n'
    '- [Native AOT Support](../advanced/aot-support) — AOT/NativeAOT compatibility guide\n'
    '- [Source Generation](../advanced/source-generation) — source generator guide'
)


def write_website_doc(doc_content: str) -> None:
    """Write the Docusaurus website mirror of BENCHMARKS.md."""
    website_content = _WEBSITE_FRONTMATTER + doc_content.replace(
        _DOCS_SEE_ALSO, _WEBSITE_SEE_ALSO
    )
    try:
        with open(WEBSITE_DOC_PATH, 'w') as f:
            f.write(website_content)
        print(f'Website benchmarks.md synced to {WEBSITE_DOC_PATH}')
    except Exception as exc:
        print(f'Warning: could not write {WEBSITE_DOC_PATH}: {exc}', file=sys.stderr)


# ---------------------------------------------------------------------------
# Baseline-only mode: push a new entry onto the ring (FIFO, max 3 entries).
# Only main-branch pushes reach this path — PR runs are never permitted to
# update the ring, which prevents in-flight branch metrics from contaminating
# the baseline used by future PR comparisons.
# ---------------------------------------------------------------------------
if BASELINE_ONLY:
    ring = parse_baseline_ring(doc)
    new_entry: dict = {k: v['mean'] for k, v in metrics.items()}
    for k, v in metrics.items():
        new_entry[f'{k}_a'] = parse_alloc_bytes(v['alloc'])

    if not ring:
        # No baseline recorded yet. Store the measurement three times so the
        # median is immediately well-defined on the very first main-branch push.
        ring = [new_entry, new_entry, new_entry]
    else:
        # Prepend the newest entry and keep the two most recent existing entries
        # (FIFO ring, max 3 total — oldest entry is evicted).
        ring = [new_entry] + ring[:2]

    new_bl_comment = ring_to_comment(ring)
    old_bl_re = re.compile(r'<!-- netmediate-bench-baseline: .+? -->', re.DOTALL)
    if old_bl_re.search(doc):
        doc = old_bl_re.sub(new_bl_comment, doc)
    else:
        doc = doc.replace(
            '# NetMediate Benchmark Results\n',
            '# NetMediate Benchmark Results\n\n' + new_bl_comment + '\n',
            1,
        )
    with open(DOC_PATH, 'w') as f:
        f.write(doc)
    write_website_doc(doc)
    print(f'Baseline-only update complete. Ring size: {len(ring)}. '
          f'Medians: cmd={median_from_ring(ring, "cmd"):.2f}ns, '
          f'notify={median_from_ring(ring, "notify"):.2f}ns')
    sys.exit(0)

# ---------------------------------------------------------------------------
# Build updated environment block (replaces the ci-environment marker region)
# ---------------------------------------------------------------------------
env_block = (
    f'| Key | Value |\n|---|---|\n'
    f'| OS | {os_str} |\n'
    f'| CPU | {cpu_str} |\n'
    f'| .NET SDK | {sdk_str} |\n'
    f'| Runtime | {host_str} |\n'
    f'| Last CI run | {DATE} |\n'
    f'| Branch | `{BRANCH}` |\n'
    f'| Commit | `{COMMIT}` |'
)

# ---------------------------------------------------------------------------
# Build updated throughput block (replaces the ci-throughput marker region)
# Columns: Mean | Error | Gen0 | Allocated | Alloc Δ | Throughput | vs timing
# ---------------------------------------------------------------------------
tput_header = (
    '| Benchmark | Mean | Error | Gen0 | Allocated | Alloc Δ | Throughput | vs timing |\n'
    '|---|---|---|---|---|---|---|---|'
)
tput_rows = []
for key in ORDERED_KEYS:
    if key in metrics:
        m = metrics[key]
        tput_rows.append(
            f"| {KEY_TO_LABEL[key]} | {m['mean']:.2f} ns | ±{m['error']:.3f} ns"
            f" | {m['gen0']:.4f} | {m['alloc']}"
            f" | {compare_alloc_str(key, m['alloc'])}"
            f" | {throughput_str(m['mean'])} | {compare_str(key, m['mean'])} |"
        )
throughput_block = tput_header + '\n' + '\n'.join(tput_rows)


def replace_between(text: str, start_marker: str, end_marker: str, new_content: str) -> str:
    pat = re.compile(re.escape(start_marker) + r'.*?' + re.escape(end_marker), re.DOTALL)
    if not pat.search(text):
        print(f'Warning: marker {start_marker!r} not found in {DOC_PATH} — block not updated.',
              file=sys.stderr)
        return text
    replacement = start_marker + '\n' + new_content + '\n' + end_marker
    return pat.sub(replacement, text)


doc = replace_between(doc, '<!-- ci-environment-start -->', '<!-- ci-environment-end -->', env_block)
doc = replace_between(doc, '<!-- ci-throughput-start -->', '<!-- ci-throughput-end -->', throughput_block)

# Note: the baseline ring is NOT updated in PR mode.  Only main-branch pushes
# (BENCH_BASELINE_ONLY=true, handled above) may modify the ring to prevent
# PR-branch metric contamination from skewing future comparisons.

# ---------------------------------------------------------------------------
# Build comparison table for the "Latest CI Benchmark Run" section
# ---------------------------------------------------------------------------
_has_base = bool(live_base_metrics) or bool(baseline_ring)
if _has_base:
    cmp_rows = [
        f'| Benchmark | Baseline (`{BASE_REF}`, median of ≤3 runs) | Current | Δ timing | Alloc Δ |',
        '|---|---|---|---|---|',
    ]
    for key in ORDERED_KEYS:
        if key in metrics:
            cur_ns   = metrics[key]['mean']
            prev_ns  = get_base_mean(key)
            alloc_cmp = compare_alloc_str(key, metrics[key]['alloc'])
            if prev_ns:
                d = (cur_ns - prev_ns) / prev_ns * 100
                if abs(d) <= THRESHOLD_PERCENT:
                    timing_status = f'≈ {d:+.1f}%'
                elif d < 0:
                    timing_status = f'✅ {d:+.1f}%'
                else:
                    timing_status = f'⚠️ {d:+.1f}%'
                cmp_rows.append(
                    f'| {KEY_TO_LABEL[key]} | {prev_ns:.2f} ns | {cur_ns:.2f} ns'
                    f' | {timing_status} | {alloc_cmp} |'
                )
            else:
                cmp_rows.append(
                    f'| {KEY_TO_LABEL[key]} | — | {cur_ns:.2f} ns | new | {alloc_cmp} |'
                )
    comparison_md = '\n'.join(cmp_rows)
else:
    comparison_md = '_No baseline available — this is the first recorded run._'

# ---------------------------------------------------------------------------
# Build the "Latest CI Benchmark Run" section (summary only, no console log)
# ---------------------------------------------------------------------------
_base_note = (
    '✅ Base branch benchmarked in the same CI job (same machine — direct comparison).'
    if _using_live_base else
    'ℹ️ Timing baseline loaded from stored target-branch docs (different run — ±10% is noise).'
)
ci_section = (
    '## Latest CI Benchmark Run\n\n'
    f'Run: {DATE} | Branch: `{BRANCH}` | Commit: `{COMMIT}`\n\n'
    f'> {_base_note}\n\n'
    '### System specification\n\n'
    '```\n'
    f'{os_str}\n'
    f'{cpu_str}\n'
    f'.NET SDK {sdk_str}\n'
    f'Runtime: {host_str}\n'
    '```\n\n'
    '### Performance summary (BenchmarkDotNet — Throughput job)\n\n'
    f'{throughput_block}\n\n'
    f'### Comparison vs baseline (`{BASE_REF}`, median of ≤3 runs)\n\n'
    f'> Timing: ✅ improved (>{THRESHOLD_PERCENT:.0f}% faster)'
    f'\u00a0|\u00a0 ≈ no change (±{THRESHOLD_PERCENT:.0f}%)'
    f'\u00a0|\u00a0 ⚠️ degraded (>{THRESHOLD_PERCENT:.0f}% slower)\n'
    '> Alloc Δ: ✅ same / ✅ −N B (less) / ⚠️ +N B (more)'
    '\n\n'
    f'{comparison_md}'
)

ci_pat = re.compile(r'^## Latest CI Benchmark Run.*', re.MULTILINE | re.DOTALL)
if ci_pat.search(doc):
    doc = ci_pat.sub(ci_section.rstrip(), doc)
else:
    doc = doc.rstrip() + '\n\n' + ci_section.rstrip() + '\n'

# ---------------------------------------------------------------------------
# Write output
# ---------------------------------------------------------------------------
with open(DOC_PATH, 'w') as f:
    f.write(doc)
write_website_doc(doc)

ring_size = len(baseline_ring)
print(f'BENCHMARKS.md updated successfully. '
      f'Baseline ring: {ring_size} entr{"y" if ring_size == 1 else "ies"} '
      f'(medians: cmd={median_from_ring(baseline_ring, "cmd") or "—"}ns, '
      f'notify={median_from_ring(baseline_ring, "notify") or "—"}ns)')
