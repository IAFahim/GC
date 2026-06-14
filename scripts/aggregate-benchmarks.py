#!/usr/bin/env python3
"""
Aggregate benchmark results from multiple platforms into a single summary.
Usage: python scripts/aggregate-benchmarks.py <results_dir> <output_json>
"""

import json
import sys
import os
from pathlib import Path
from datetime import datetime


def parse_csv_to_dict(csv_path):
    """Parse BenchmarkDotNet CSV file into a dictionary."""
    import csv
    import re

    results = {}

    try:
        with open(csv_path, 'r') as f:
            reader = csv.DictReader(f)
            for row in reader:
                method = row['Method']

                # Parse Mean time
                mean_raw = row['Mean'].replace(',', '').replace(' ', '')
                mean_match = re.match(r'([\d.]+)(\w+)', mean_raw)
                if mean_match:
                    mean_value = float(mean_match.group(1))
                    unit = mean_match.group(2)
                    # Convert to nanoseconds
                    unit_multipliers = {'ns': 1, 'us': 1000, 'ms': 1000000, 's': 1000000000}
                    mean_ns = mean_value * unit_multipliers.get(unit, 1)
                else:
                    mean_ns = 0

                # Parse Allocated memory
                alloc_raw = row['Allocated'].replace(' ', '')
                alloc_match = re.match(r'([\d.]+)\s*(\w+)', alloc_raw)
                if alloc_match:
                    alloc_value = float(alloc_match.group(1))
                    alloc_unit = alloc_match.group(2)
                    unit_multipliers = {'B': 1, 'KB': 1024, 'MB': 1024 * 1024}
                    allocated_bytes = int(alloc_value * unit_multipliers.get(alloc_unit, 1))
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
                    'stddev_ns': 0,
                }
    except Exception as e:
        print(f"Error parsing CSV {csv_path}: {e}", file=sys.stderr)

    return results


def load_json(json_path):
    """Load JSON file."""
    try:
        with open(json_path, 'r') as f:
            return json.load(f)
    except Exception as e:
        print(f"Error loading JSON {json_path}: {e}", file=sys.stderr)
        return None


def determine_platform(file_path):
    """Determine platform from file path."""
    file_path_lower = str(file_path).lower()
    if 'ubuntu' in file_path_lower or 'linux' in file_path_lower:
        return 'linux'
    elif 'macos' in file_path_lower or 'darwin' in file_path_lower:
        return 'macos'
    elif 'windows' in file_path_lower:
        return 'windows'
    return 'unknown'


def aggregate_results(results_dir, output_path):
    """Aggregate benchmark results from multiple platforms."""

    results_dir = Path(results_dir)
    output_path = Path(output_path)

    aggregated = {
        'metadata': {
            'generated': datetime.utcnow().isoformat() + 'Z',
            'platforms': set(),
        },
        'benchmarks': {}
    }

    # Find all result files
    json_files = list(results_dir.rglob('*.json'))
    csv_files = list(results_dir.rglob('*.csv'))

    # Process JSON files (already processed benchmarks)
    for json_file in json_files:
        data = load_json(json_file)
        if data and 'benchmarks' in data:
            platform = determine_platform(json_file)
            aggregated['metadata']['platforms'].add(platform)

            for name, values in data['benchmarks'].items():
                if name not in aggregated['benchmarks']:
                    aggregated['benchmarks'][name] = {'platforms': {}}

                aggregated['benchmarks'][name]['platforms'][platform] = values

    # Process CSV files (raw BenchmarkDotNet output)
    for csv_file in csv_files:
        platform = determine_platform(csv_file)
        aggregated['metadata']['platforms'].add(platform)

        results = parse_csv_to_dict(csv_file)

        for name, values in results.items():
            if name not in aggregated['benchmarks']:
                aggregated['benchmarks'][name] = {'platforms': {}}

            aggregated['benchmarks'][name]['platforms'][platform] = values

    # Convert set to list for JSON serialization
    aggregated['metadata']['platforms'] = list(aggregated['metadata']['platforms'])

    # Calculate averages across platforms for each benchmark
    for name, data in aggregated['benchmarks'].items():
        platforms = list(data['platforms'].values())
        if platforms:
            data['average'] = {
                'mean_ns': sum(p.get('mean_ns', 0) for p in platforms) / len(platforms),
                'allocated_bytes': sum(p.get('allocated_bytes', 0) for p in platforms) / len(platforms),
                'sample_count': len(platforms)
            }

    # Write output
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, 'w') as f:
        json.dump(aggregated, f, indent=2)

    print(f"Aggregated results written to {output_path}")
    print(f"Found {len(aggregated['benchmarks'])} benchmarks across {len(aggregated['metadata']['platforms'])} platforms")

    return aggregated


if __name__ == '__main__':
    if len(sys.argv) < 3:
        print("Usage: python aggregate-benchmarks.py <results_dir> <output_json>")
        sys.exit(1)

    results_dir = sys.argv[1]
    output_path = sys.argv[2]

    try:
        aggregate_results(results_dir, output_path)
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)
