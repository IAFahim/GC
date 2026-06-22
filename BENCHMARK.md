# Real Performance Data

Measured on the published NativeAOT binary (the shipped artifact), best of 5 warm runs:

## This repository (small workload)

| Metric | Value |
|--------|-------|
| Discovery Time | 2 ms |
| File Read Time | 5 ms |
| Total Time | 7 ms |

## Synthetic large workload (~1,500 files incl. large files)

Exercises the parallel read + generate pipeline at scale.

| Metric | Value |
|--------|-------|
| Files | 1,505 |
| Read Time | 18 ms |
| Throughput | 278.89 MB/s |

*Last updated: 2026-06-22 17:00:50 UTC*
