#!/bin/bash
# Compare-Benchmarks.sh
# Bash script to compare benchmark results against baseline
# Usage: ./scripts/compare-benchmarks.sh <results_dir> <baseline.json> [--summary]

set -e

RESULTS_DIR="${1:-results/}"
BASELINE_PATH="${2:-benchmarks/baseline.json}"
GENERATE_SUMMARY=false
PERFORMANCE_THRESHOLD=${PERFORMANCE_THRESHOLD:-5}
MEMORY_THRESHOLD=${MEMORY_THRESHOLD:-10}

# Parse arguments
shift 2
while [[ $# -gt 0 ]]; do
    case $1 in
        --summary)
            GENERATE_SUMMARY=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Colors for terminal output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Find all result JSON files
RESULT_FILES=$(find "$RESULTS_DIR" -name "*.json" -type f 2>/dev/null || true)

if [[ -z "$RESULT_FILES" ]]; then
    echo "No benchmark result files found in $RESULTS_DIR"
    exit 1
fi

# Function to extract metric from JSON
get_metric() {
    local file=$1
    local benchmark=$2
    local metric=$3
    jq -r ".benchmarks.\"$benchmark\".current.$metric // empty" "$file" 2>/dev/null || echo ""
}

# Function to parse BenchmarkDotNet CSV to JSON
parse_csv_to_json() {
    local csv_file=$1
    local output_file=$2

    python3 -c "
import csv
import json
import re
from datetime import datetime

results = {}
try:
    with open('$csv_file', 'r') as f:
        reader = csv.DictReader(f)
        for row in reader:
            method = row['Method']

            # Parse Mean (e.g., '3,131.4 ns' -> 3131.4)
            mean_raw = row['Mean'].replace(',', '').replace(' ', '')
            mean_match = re.match(r'([\d.]+)(\w+)', mean_raw)
            if mean_match:
                mean_value = float(mean_match.group(1))
                unit = mean_match.group(2)
                # Convert to nanoseconds
                if unit == 'ns':
                    mean_ns = mean_value
                elif unit == 'us':
                    mean_ns = mean_value * 1000
                elif unit == 'ms':
                    mean_ns = mean_value * 1000000
                elif unit == 's':
                    mean_ns = mean_value * 1000000000
                else:
                    mean_ns = mean_value
            else:
                mean_ns = 0

            # Parse Allocated
            alloc_raw = row['Allocated'].replace(' ', '')
            alloc_match = re.match(r'([\d.]+)\s*(\w+)', alloc_raw)
            if alloc_match:
                alloc_value = float(alloc_match.group(1))
                alloc_unit = alloc_match.group(2)
                if alloc_unit == 'B':
                    allocated_bytes = int(alloc_value)
                elif alloc_unit == 'KB':
                    allocated_bytes = int(alloc_value * 1024)
                elif alloc_unit == 'MB':
                    allocated_bytes = int(alloc_value * 1024 * 1024)
                else:
                    allocated_bytes = int(alloc_value)
            else:
                allocated_bytes = 0

            # Parse GC collections
            def parse_gc(val):
                if val in ('Default', ''):
                    return 0
                try:
                    return float(val)
                except:
                    return 0

            results[method] = {
                'mean_ns': mean_ns,
                'allocated_bytes': allocated_bytes,
                'gen0_collections': parse_gc(row.get('Gen0', '0')),
                'gen1_collections': parse_gc(row.get('Gen1', '0')),
                'gen2_collections': parse_gc(row.get('Gen2', '0')),
                'error_ns': 0,
                'stddev_ns': 0
            }

    output = {
        'metadata': {
            'generated': datetime.utcnow().isoformat() + 'Z',
            'platform': '$RUNNER_OS',
            'source': '$csv_file'
        },
        'benchmarks': results
    }

    with open('$output_file', 'w') as f:
        json.dump(output, f, indent=2)
except Exception as e:
    print(f'Error parsing CSV: {e}', file=__import__('sys').stderr)
    import sys
    sys.exit(1)
"
}

# Parse all CSV files to JSON
declare -A platform_data
for csv_file in $(find "$RESULTS_DIR" -name "*.csv" -type f 2>/dev/null); do
    # Extract platform from path or filename
    platform_json="${csv_file%.csv}.json"
    parse_csv_to_json "$csv_file" "$platform_json"

    # Determine platform from directory structure
    if [[ "$csv_file" =~ ubuntu ]]; then
        platform="linux"
    elif [[ "$csv_file" =~ macos ]]; then
        platform="macos"
    elif [[ "$csv_file" =~ windows ]]; then
        platform="windows"
    else
        platform="unknown"
    fi

    platform_data[$platform]=$platform_json
done

# Aggregate results across platforms
aggregate_json="/tmp/aggregate.json"
python3 -c "
import json
import sys

benchmarks = {}
for platform, json_file in [('$(echo ${platform_data[@]})'.replace(' ', \"'),('\"))].split('),('):
    if not json_file:
        continue
    try:
        with open(json_file.strip(\"',\"), 'r') as f:
            data = json.load(f)
            for name, values in data.get('benchmarks', {}).items():
                if name not in benchmarks:
                    benchmarks[name] = {'platforms': {}}
                benchmarks[name]['platforms'][json_file.strip(\"'\")] = values
    except:
        pass

output = {
    'metadata': {
        'generated': '$(date -u +"%Y-%m-%dT%H:%M:%SZ")',
        'platforms': list(set(['$(echo ${!platform_data[@]})'.replace(' ', \"','\"')].split(',')))
    },
    'benchmarks': benchmarks
}

with open('$aggregate_json', 'w') as f:
    json.dump(output, f, indent=2)
"

# Generate summary for PR comment
if [[ "$GENERATE_SUMMARY" == true ]]; then
    cat > performance-summary.md << 'EOF'
## 🧪 Performance Test Results

Benchmark comparison against main branch baseline.

EOF

    # Check if we have baseline
    if [[ -f "$BASELINE_PATH" ]]; then
        echo "Comparing against baseline: $BASELINE_PATH" >&2

        # Run comparison using Python
        python3 -c "
import json
import os

with open('$aggregate_json', 'r') as f:
    current = json.load(f)

with open('$BASELINE_PATH', 'r') as f:
    baseline = json.load(f)

thresholds = baseline.get('thresholds', {})
perf_thresh = float(os.getenv('PERFORMANCE_THRESHOLD', thresholds.get('performance_regression_percent', 5)))
mem_thresh = float(os.getenv('MEMORY_THRESHOLD', thresholds.get('memory_regression_percent', 10)))

regressions = []
improvements = []
summary_table = []

for name, current_data in current.get('benchmarks', {}).items():
    baseline_data = baseline.get('benchmarks', {}).get(name)

    if not baseline_data:
        continue

    # Get average values across platforms
    platforms = list(current_data.get('platforms', {}).values())
    if not platforms:
        continue

    current_mean = sum(p.get('mean_ns', 0) for p in platforms) / len(platforms)
    current_alloc = sum(p.get('allocated_bytes', 0) for p in platforms) / len(platforms)

    baseline_mean = baseline_data.get('mean_ns', 0)
    baseline_alloc = baseline_data.get('allocated_bytes', 0)

    # Calculate deltas
    if baseline_mean > 0:
        perf_delta = ((current_mean - baseline_mean) / baseline_mean) * 100
    else:
        perf_delta = 0

    if baseline_alloc > 0:
        mem_delta = ((current_alloc - baseline_alloc) / baseline_alloc) * 100
    else:
        mem_delta = 100 if current_alloc > 0 else 0

    status = '✅'
    if perf_delta > perf_thresh:
        status = '⚠️'
        regressions.append((name, 'Performance', perf_delta, perf_thresh))
    if mem_delta > mem_thresh:
        status = '⚠️'
        regressions.append((name, 'Memory', mem_delta, mem_thresh))
    if perf_delta < -perf_thresh:
        improvements.append((name, 'Performance', perf_delta))

    summary_table.append({
        'name': name,
        'current_ms': round(current_mean / 1_000_000, 2),
        'baseline_ms': round(baseline_mean / 1_000_000, 2),
        'delta_pct': round(perf_delta, 1),
        'status': status
    })

# Output markdown table
print('| Benchmark | Current (ms) | Baseline (ms) | Delta | Status |')
print('|-----------|--------------|---------------|-------|--------|')
for row in sorted(summary_table, key=lambda x: x['delta_pct'], reverse=True):
    delta_str = f\"{row['delta_pct']:+.1f}%\"
    print(f\"| {row['name']} | {row['current_ms']} | {row['baseline_ms']} | {delta_str} | {row['status']} |\")

print()

if regressions:
    print('### ⚠️ Performance Regressions Detected')
    print()
    for name, type, delta, thresh in regressions:
        print(f'- **{name}**: {type} regression of **+{delta:.1f}%** (threshold: {thresh}%)')
    print()
    print('Please review these changes before merging.')
    print()

if improvements:
    print('### 🎉 Performance Improvements')
    print()
    for name, type, delta in improvements:
        print(f'- **{name}**: {type} improvement of **{delta:.1f}%**')
    print()

if not regressions and not improvements:
    print('No significant performance changes detected.')
    print()

exit(1 if regressions else 0)
" 2>&1 | tee -a performance-summary.md

        exit_code=${PIPESTATUS[0]}

        if [[ $exit_code -ne 0 ]]; then
            echo "" >> performance-summary.md
            echo "<!-- has_regression=true -->" >> performance-summary.md
        fi

    else
        echo "No baseline available for comparison." >> performance-summary.md
        echo "" >> performance-summary.md
        echo "This is the first performance run. Results will be stored as baseline." >> performance-summary.md
    fi

    cat performance-summary.md
    exit 0
fi

# Normal comparison mode (for CI checks)
if [[ -f "$BASELINE_PATH" ]]; then
    python3 -c "
import json
import sys

with open('$aggregate_json', 'r') as f:
    current = json.load(f)

with open('$BASELINE_PATH', 'r') as f:
    baseline = json.load(f)

thresholds = baseline.get('thresholds', {})
perf_thresh = float(os.getenv('PERFORMANCE_THRESHOLD', thresholds.get('performance_regression_percent', 5)))
mem_thresh = float(os.getenv('MEMORY_THRESHOLD', thresholds.get('memory_regression_percent', 10)))

regression_count = 0

for name, current_data in current.get('benchmarks', {}).items():
    baseline_data = baseline.get('benchmarks', {}).get(name)

    if not baseline_data:
        print(f\"No baseline for {name}\")
        continue

    platforms = list(current_data.get('platforms', {}).values())
    if not platforms:
        continue

    current_mean = sum(p.get('mean_ns', 0) for p in platforms) / len(platforms)
    current_alloc = sum(p.get('allocated_bytes', 0) for p in platforms) / len(platforms)

    baseline_mean = baseline_data.get('mean_ns', 0)
    baseline_alloc = baseline_data.get('allocated_bytes', 0)

    if baseline_mean > 0:
        perf_delta = ((current_mean - baseline_mean) / baseline_mean) * 100
    else:
        perf_delta = 0

    if baseline_alloc > 0:
        mem_delta = ((current_alloc - baseline_alloc) / baseline_alloc) * 100
    else:
        mem_delta = 100 if current_alloc > 0 else 0

    if perf_delta > perf_thresh:
        print(f\"REGRESSION: {name} - Performance +{perf_delta:.1f}% (threshold: {perf_thresh}%)\", file=sys.stderr)
        regression_count += 1

    if mem_delta > mem_thresh:
        print(f\"REGRESSION: {name} - Memory +{mem_delta:.1f}% (threshold: {mem_thresh}%)\", file=sys.stderr)
        regression_count += 1

if regression_count > 0:
    print(f'has_regression=true', file=open(os.getenv('GITHUB_OUTPUT', '/dev/null'), 'a'))
    sys.exit(1)
else:
    print('✅ No performance regressions detected')
    sys.exit(0)
"

    exit $?
else
    echo "⚠️  No baseline file found at $BASELINE_PATH"
    echo "Results will be stored as new baseline on main branch merge."
    exit 0
fi
