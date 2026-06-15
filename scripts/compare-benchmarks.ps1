# Compare-Benchmarks.ps1
# PowerShell script to compare benchmark results against baseline
# Usage: .\scripts\compare-benchmarks.ps1 -ResultsPath <path> -BaselinePath <path> [-OutputPath <path>] [-Platform <platform>]

param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsPath,

    [Parameter(Mandatory = $false)]
    [string]$BaselinePath = "benchmarks/baseline.json",

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = "benchmarks/current-results.json",

    [Parameter(Mandatory = $false)]
    [string]$Platform = $env:RUNNER_OS,

    [Parameter(Mandatory = $false)]
    [double]$PerformanceThreshold = 5.0,

    [Parameter(Mandatory = $false)]
    [double]$MemoryThreshold = 10.0,

    [Parameter(Mandatory = $false)]
    [switch]$Summary,

    # When set, exit 1 if regressions are detected. Off by default: the benchmark job records results
    # (and, on push to main, a new baseline); PR gating is the dedicated regression-check job's job.
    [Parameter(Mandatory = $false)]
    [switch]$FailOnRegression
)

$ErrorActionPreference = "Stop"

# BenchmarkDotNet leaves empty numeric cells as "-" (and may omit columns entirely). A raw [double]
# cast on those throws, so normalize them to 0 here.
function ConvertTo-DoubleSafe {
    param($Value)
    if ([string]::IsNullOrWhiteSpace([string]$Value)) { return 0.0 }
    $s = ([string]$Value).Trim()
    if ($s -in @('-', 'Default', 'NA', 'N/A')) { return 0.0 }
    $parsed = 0.0
    if ([double]::TryParse($s, [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        return $parsed
    }
    return 0.0
}

# Function to parse BenchmarkDotNet CSV
function Get-BenchmarkResults {
    param([string]$CsvPath)

    if (-not (Test-Path $CsvPath)) {
        throw "Benchmark results file not found: $CsvPath"
    }

    $results = @{}
    $csv = Import-Csv -Path $CsvPath

    foreach ($row in $csv) {
        $methodName = $row.Method

        # Parse Mean (e.g., "3,131.4 ns" -> 3131.4)
        $meanRaw = $row.Mean -replace '[, ]', ''
        if ($meanRaw -match '([\d.]+)\s*(\w+)') {
            $meanValue = [double]$matches[1]
            $unit = $matches[2]

            # Convert to nanoseconds
            switch ($unit) {
                'ns' { $meanNs = $meanValue }
                'us' { $meanNs = $meanValue * 1000 }
                'ms' { $meanNs = $meanValue * 1000000 }
                's' { $meanNs = $meanValue * 1000000000 }
                default { $meanNs = $meanValue }
            }
        } else {
            $meanNs = 0
        }

        # Parse Allocated (e.g., "0 B" -> 0, "3584 B" -> 3584)
        $allocatedRaw = $row.Allocated -replace '\s+', ''
        if ($allocatedRaw -match '([\d.]+)\s*(\w+)') {
            $allocatedValue = [double]$matches[1]
            $allocatedUnit = $matches[2]

            switch ($allocatedUnit) {
                'B' { $allocatedBytes = [int]$allocatedValue }
                'KB' { $allocatedBytes = [int]($allocatedValue * 1024) }
                'MB' { $allocatedBytes = [int]($allocatedValue * 1024 * 1024) }
                default { $allocatedBytes = [int]$allocatedValue }
            }
        } else {
            $allocatedBytes = 0
        }

        # Parse GC collections (cells may be "-", blank, or absent)
        $gen0 = ConvertTo-DoubleSafe $row.Gen0
        $gen1 = ConvertTo-DoubleSafe $row.Gen1
        $gen2 = ConvertTo-DoubleSafe $row.Gen2

        $results[$methodName] = @{
            mean_ns            = $meanNs
            allocated_bytes    = $allocatedBytes
            gen0_collections   = $gen0
            gen1_collections   = $gen1
            gen2_collections   = $gen2
            error_ns           = 0
            stddev_ns          = 0
        }
    }

    return $results
}

# Function to load baseline JSON
function Get-Baseline {
    param([string]$JsonPath)

    if (-not (Test-Path $JsonPath)) {
        Write-Host "Baseline file not found: $JsonPath - using empty baseline"
        return @{}
    }

    # -AsHashtable (PowerShell 6+) is required: plain ConvertFrom-Json yields a PSCustomObject, which
    # cannot bind to Compare-Results' [hashtable]$Baseline parameter or expose .ContainsKey().
    $json = Get-Content -Path $JsonPath -Raw | ConvertFrom-Json -AsHashtable
    if ($null -eq $json -or $null -eq $json.benchmarks) {
        return @{}
    }
    return $json.benchmarks
}

# Function to compare results
function Compare-Results {
    param(
        [hashtable]$Current,
        [hashtable]$Baseline
    )

    $comparison = @{
        metadata = @{
            generated     = (Get-Date).ToUniversalTime().ToString("o")
            platform      = $Platform
            git_commit    = $env:GITHUB_SHA
            git_ref       = $env:GITHUB_REF
        }
        benchmarks      = @{}
        regressions     = @()
        improvements    = @()
    }

    foreach ($benchmark in $Current.Keys) {
        $currentValue = $Current[$benchmark]
        $baselineValue = if ($Baseline.ContainsKey($benchmark)) { $Baseline[$benchmark] } else { $null }

        $benchmarkData = @{
            current = @{
                mean_ns         = $currentValue.mean_ns
                allocated_bytes = $currentValue.allocated_bytes
            }
            baseline = if ($baselineValue) {
                @{
                    mean_ns         = $baselineValue.mean_ns
                    allocated_bytes = $baselineValue.allocated_bytes
                }
            } else { $null }
        }

        if ($baselineValue) {
            # Calculate performance delta
            if ($baselineValue.mean_ns -gt 0) {
                $perfDelta = (($currentValue.mean_ns - $baselineValue.mean_ns) / $baselineValue.mean_ns) * 100
            } else {
                $perfDelta = 0
            }

            # Calculate memory delta
            if ($baselineValue.allocated_bytes -gt 0) {
                $memDelta = (($currentValue.allocated_bytes - $baselineValue.allocated_bytes) / $baselineValue.allocated_bytes) * 100
            } else {
                $memDelta = if ($currentValue.allocated_bytes -gt 0) { 100 } else { 0 }
            }

            $benchmarkData.performance_delta_percent = [Math]::Round($perfDelta, 2)
            $benchmarkData.memory_delta_percent = [Math]::Round($memDelta, 2)

            # Check for regression
            $status = "OK"
            if ($perfDelta -gt $PerformanceThreshold) {
                $status = "REGRESSION"
                $comparison.regressions += @{
                    benchmark = $benchmark
                    type      = "performance"
                    delta     = $perfDelta
                    threshold = $PerformanceThreshold
                }
            }

            if ($memDelta -gt $MemoryThreshold) {
                $status = "REGRESSION"
                $comparison.regressions += @{
                    benchmark = $benchmark
                    type      = "memory"
                    delta     = $memDelta
                    threshold = $MemoryThreshold
                }
            }

            if ($perfDelta -lt -$PerformanceThreshold) {
                $comparison.improvements += @{
                    benchmark = $benchmark
                    type      = "performance"
                    delta     = $perfDelta
                }
            }

            $benchmarkData.status = $status
        }

        $comparison.benchmarks[$benchmark] = $benchmarkData
    }

    return $comparison
}

# Main execution
try {
    Write-Host "Loading benchmark results from: $ResultsPath"
    $current = Get-BenchmarkResults -CsvPath $ResultsPath

    Write-Host "Loading baseline from: $BaselinePath"
    $baseline = Get-Baseline -JsonPath $BaselinePath

    Write-Host "Comparing results..."
    $comparison = Compare-Results -Current $current -Baseline $baseline

    # Convert to JSON and save
    $jsonOutput = $comparison | ConvertTo-Json -Depth 10
    $jsonOutput | Out-File -FilePath $OutputPath -Encoding UTF8

    Write-Host "Results saved to: $OutputPath"

    # Output summary
    if ($comparison.regressions.Count -gt 0) {
        Write-Host "`n⚠️  PERFORMANCE REGRESSIONS DETECTED" -ForegroundColor Yellow
        foreach ($regression in $comparison.regressions) {
            $color = if ($regression.delta -gt $regression.threshold * 2) { "Red" } else { "Yellow" }
            Write-Host "  - $($regression.benchmark): $($regression.type) +$($regression.delta)%" -ForegroundColor $color
        }
    }

    if ($comparison.improvements.Count -gt 0) {
        Write-Host "`n✅ Performance Improvements:"
        foreach ($improvement in $comparison.improvements) {
            Write-Host "  - $($improvement.benchmark): $($improvement.type) $($improvement.delta)%"
        }
    }

    # Record the regression flag for GitHub Actions (guarded so local runs don't crash on a null var).
    if ($env:GITHUB_OUTPUT) {
        $flag = if ($comparison.regressions.Count -gt 0) { "true" } else { "false" }
        "has_regression=$flag" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }

    if ($comparison.regressions.Count -gt 0) {
        if ($FailOnRegression) { exit 1 }
        Write-Host "`n(Regressions recorded; not failing the build — gating is handled by the regression-check job.)"
        exit 0
    }

    Write-Host "`n✅ No performance regressions detected"
    exit 0

} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}
