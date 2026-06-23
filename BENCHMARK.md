# Real Performance Data

Measured on the published NativeAOT binary (the shipped artifact), best of 5 warm runs:

## This repository (small workload)

| Metric | Value |
|--------|-------|
| Discovery Time | 2 ms |
| File Read Time | 4 ms |
| Total Time | 6 ms |

## Synthetic large workload (~1,500 files incl. large files)

Exercises the parallel read + generate pipeline at scale.

| Metric | Value |
|--------|-------|
| Files | 1,505 |
| Read Time | 17 ms |
| Throughput | 308.61 MB/s |

*Last updated: 2026-06-23 05:47:17 UTC*
