#!/usr/bin/env python3
"""
Update docs/BENCHMARKS.md with the latest BenchmarkDotNet results.

Called by CI after running the benchmarks.  Reads three files:
  /tmp/bench-report.md  – BenchmarkDotNet GitHub markdown report
  /tmp/benchmarks-base.md – BENCHMARKS.md from the target branch (for baseline)
  docs/BENCHMARKS.md  – the file to be updated (in the current workspace)

Environment variables consumed:
  BRANCH      – current PR head branch name
  COMMIT_SHA  – full SHA of the head commit
  BASE_REF    – target branch name (for labelling the baseline column)
  BENCHMARKS_MD – path to the doc to update (defaults to docs/BENCHMARKS.md)
"""
import re
import json
import sys
import os
from datetime import datetime, timezone

BRANCH   = os.environ.get('BRANCH', 'unknown')
COMMIT   = os.environ.get('COMMIT_SHA', 'unknown')[:7]
BASE_REF = os.environ.get('BASE_REF', 'main')
DATE     = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M UTC')
DOC_PATH = os.environ.get('BENCHMARKS_MD', 'docs/BENCHMARKS.md')

# ---------------------------------------------------------------------------
# Read input files
# ---------------------------------------------------------------------------
with open('/tmp/bench-report.md') as f:
    report = f.read()
with open('/tmp/benchmarks-base.md') as f:
    base_doc = f.read()
with open(DOC_PATH) as f:
    doc = f.read()

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

# Parse Throughput-job rows from the BenchmarkDotNet GitHub markdown report.
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
metrics: dict[str, dict] = {}
for m in row_re.finditer(report):
    method = m.group(1)
    for method_name, (key, _) in BENCHMARKS.items():
        if method_name in method:
            metrics[key] = {
                'mean':  float(m.group(2)),
                'error': float(m.group(3)),
                'gen0':  float(m.group(4)),
                'alloc': m.group(5).strip(),
            }
            break

if not metrics:
    print(
        'Warning: no Throughput-job rows found in bench-report.md — skipping update.',
        file=sys.stderr,
    )
    sys.exit(0)

# ---------------------------------------------------------------------------
# Extract system info from the report's fenced ini block at the top
# ---------------------------------------------------------------------------
os_m    = re.search(r'BenchmarkDotNet v[\d.]+, (.+)', report)
cpu_m   = re.search(r'^([ \t]*[\w][ \t\w\d.()/,]*GHz[^\n]*)', report, re.MULTILINE)
sdk_m   = re.search(r'\.NET SDK ([\d.]+)', report)
host_m  = re.search(r'\[Host\]\s+: (.+)', report)
os_str   = os_m.group(1).strip()   if os_m   else 'unknown'
cpu_str  = cpu_m.group(1).strip()  if cpu_m  else 'unknown'
sdk_str  = sdk_m.group(1).strip()  if sdk_m  else 'unknown'
host_str = host_m.group(1).strip() if host_m else 'unknown'

# ---------------------------------------------------------------------------
# Read baseline from base-branch doc (stored as an HTML comment)
# ---------------------------------------------------------------------------
baseline: dict[str, float] = {}
bl_m = re.search(r'<!-- netmediate-bench-baseline: ({.+?}) -->', base_doc, re.DOTALL)
if bl_m:
    try:
        baseline = json.loads(bl_m.group(1))
    except Exception:
        pass

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
THRESHOLD_PERCENT = 3.0  # ±3% band for "no change" classification


def throughput_str(ns: float) -> str:
    t = 1e9 / ns
    return f'~{t/1e6:.1f}M msg/s' if t >= 1e6 else f'~{t/1e3:.0f}K msg/s'


def compare_str(key: str, new_ns: float) -> str:
    old_ns = baseline.get(key)
    if old_ns is None:
        return '—'
    d = (new_ns - old_ns) / old_ns * 100
    if abs(d) <= THRESHOLD_PERCENT:
        return f'≈ ({d:+.1f}%)'
    return f'✅ improved ({d:+.1f}%)' if d < 0 else f'⚠️ degraded ({d:+.1f}%)'


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
# ---------------------------------------------------------------------------
tput_header = (
    '| Benchmark | Mean | Error | Gen0 | Allocated | Throughput | vs baseline |\n'
    '|---|---|---|---|---|---|---|'
)
tput_rows = []
for key in ORDERED_KEYS:
    if key in metrics:
        m = metrics[key]
        tput_rows.append(
            f"| {KEY_TO_LABEL[key]} | {m['mean']:.2f} ns | ±{m['error']:.3f} ns"
            f" | {m['gen0']:.4f} | {m['alloc']}"
            f" | {throughput_str(m['mean'])} | {compare_str(key, m['mean'])} |"
        )
throughput_block = tput_header + '\n' + '\n'.join(tput_rows)


def replace_between(text: str, start_marker: str, end_marker: str, new_content: str) -> str:
    pat = re.compile(re.escape(start_marker) + r'.*?' + re.escape(end_marker), re.DOTALL)
    replacement = start_marker + '\n' + new_content + '\n' + end_marker
    return pat.sub(replacement, text) if pat.search(text) else text


doc = replace_between(doc, '<!-- ci-environment-start -->', '<!-- ci-environment-end -->', env_block)
doc = replace_between(doc, '<!-- ci-throughput-start -->', '<!-- ci-throughput-end -->', throughput_block)

# ---------------------------------------------------------------------------
# Update the baseline HTML comment (stores new values for next run's comparison)
# ---------------------------------------------------------------------------
new_baseline = {k: v['mean'] for k, v in metrics.items()}
new_bl_comment = '<!-- netmediate-bench-baseline: ' + json.dumps(new_baseline) + ' -->'
old_bl_pat = re.compile(r'<!-- netmediate-bench-baseline: .+? -->', re.DOTALL)
if old_bl_pat.search(doc):
    doc = old_bl_pat.sub(new_bl_comment, doc)
else:
    doc = doc.replace(
        '# NetMediate Benchmark Results\n',
        '# NetMediate Benchmark Results\n\n' + new_bl_comment + '\n',
        1,
    )

# ---------------------------------------------------------------------------
# Build comparison table for the "Latest CI Benchmark Run" section
# ---------------------------------------------------------------------------
if baseline:
    cmp_rows = [
        f'| Benchmark | Baseline (`{BASE_REF}`) | Current | Δ |',
        '|---|---|---|---|',
    ]
    for key in ORDERED_KEYS:
        if key in metrics:
            cur  = metrics[key]['mean']
            prev = baseline.get(key)
            if prev:
                d = (cur - prev) / prev * 100
                if abs(d) <= THRESHOLD_PERCENT:
                    status = f'≈ {d:+.1f}%'
                elif d < 0:
                    status = f'✅ {d:+.1f}%'
                else:
                    status = f'⚠️ {d:+.1f}%'
                cmp_rows.append(f'| {KEY_TO_LABEL[key]} | {prev:.2f} ns | {cur:.2f} ns | {status} |')
            else:
                cmp_rows.append(f'| {KEY_TO_LABEL[key]} | — | {cur:.2f} ns | new |')
    comparison_md = '\n'.join(cmp_rows)
else:
    comparison_md = '_No baseline available — this is the first recorded run._'

# ---------------------------------------------------------------------------
# Build the "Latest CI Benchmark Run" section (summary only, no console log)
# ---------------------------------------------------------------------------
ci_section = (
    '## Latest CI Benchmark Run\n\n'
    f'Run: {DATE} | Branch: `{BRANCH}` | Commit: `{COMMIT}`\n\n'
    '### System specification\n\n'
    '```\n'
    f'{os_str}\n'
    f'{cpu_str}\n'
    f'.NET SDK {sdk_str}\n'
    f'Runtime: {host_str}\n'
    '```\n\n'
    '### Performance summary (BenchmarkDotNet — Throughput job)\n\n'
    f'{throughput_block}\n\n'
    f'### Comparison vs baseline (`{BASE_REF}`)\n\n'
    f'> ✅ improved (>{THRESHOLD_PERCENT:.0f}% faster, lower ns)'
    f' \u00a0|\u00a0 ≈ no change (±{THRESHOLD_PERCENT:.0f}%)'
    f' \u00a0|\u00a0 ⚠️ degraded (>{THRESHOLD_PERCENT:.0f}% slower, higher ns)'
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

print(f'BENCHMARKS.md updated successfully. New baseline: {new_baseline}')
