# OpenXml integration performance

Measured C# source: `767f4e2f4754067332e9b20c6fef55a5f520bc50`. Release / en-US / identical NotoSansJP-Regular.ttf. Each backend case uses six separate processes: one warmup, then five measured samples. Medians follow.

The main matrix uses bounded used-column styles. `geometry-header` has one merged header, expanding print area and repeating title; its Auto measurements deliberately use ClosedXML. See the Japanese validation report for exact fixture preparation. `Cells` counts retained ReportCells; mapping alone creates none. Allocations are cumulative managed allocations, not retained memory. RSS includes native allocations.

| Stage | Case | Rows | Backend | ms | Alloc MiB | Peak RSS MiB | Cells | XLWorkbook | Spool bytes |
| --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| mapping | plain | 10,000 | closedxml | 1,525.4 | 65.9 | 132.1 | 0 | 1 | 0 |
| mapping | plain | 10,000 | openxml | 1,685.1 | 171.9 | 198.8 | 0 | 0 | 0 |
| mapping | plain | 10,000 | auto | 1,314.3 | 171.9 | 197.7 | 0 | 0 | 0 |
| mapping | nested | 10,000 | closedxml | 1,333.3 | 72.1 | 138.4 | 0 | 1 | 0 |
| mapping | nested | 10,000 | openxml | 1,187.7 | 177.7 | 198.8 | 0 | 0 | 0 |
| mapping | nested | 10,000 | auto | 1,114.7 | 177.7 | 199.0 | 0 | 0 | 0 |
| mapping | geometry-header | 10,000 | closedxml | 3,124.4 | 1,213.1 | 225.7 | 0 | 1 | 0 |
| mapping | geometry-header | 10,000 | openxml | 1,097.2 | 172.2 | 197.9 | 0 | 0 | 0 |
| mapping | geometry-header | 10,000 | auto | 3,217.6 | 1,215.4 | 226.3 | 0 | 1 | 0 |
| reader | plain | 10,000 | closedxml | 3,493.8 | 163.2 | 239.1 | 20,002 | 1 | 0 |
| reader | plain | 10,000 | openxml | 2,307.1 | 115.1 | 192.9 | 20,002 | 0 | 0 |
| reader | plain | 10,000 | auto | 2,988.7 | 143.6 | 224.0 | 20,002 | 0 | 0 |
| render | plain | 10,000 | closedxml | 5,205.5 | 391.7 | 254.8 | 20,002 | 1 | 0 |
| render | plain | 10,000 | openxml | 3,980.7 | 343.6 | 255.4 | 20,002 | 0 | 0 |
| mapping-render | plain | 10,000 | closedxml | 6,331.7 | 439.9 | 264.8 | 20,002 | 2 | 0 |
| mapping-render | plain | 10,000 | openxml | 4,685.1 | 511.5 | 246.8 | 20,002 | 0 | 0 |
| mapping | plain | 100,000 | closedxml | 3,631.4 | 515.5 | 291.1 | 0 | 1 | 0 |
| mapping | plain | 100,000 | openxml | 4,009.8 | 1,658.9 | 271.7 | 0 | 0 | 0 |
| mapping | plain | 100,000 | auto | 4,430.7 | 1,659.1 | 271.8 | 0 | 0 | 0 |
| mapping | nested | 100,000 | closedxml | 4,161.9 | 568.8 | 324.8 | 0 | 1 | 0 |
| mapping | nested | 100,000 | openxml | 4,395.6 | 1,718.8 | 300.6 | 0 | 0 | 0 |
| mapping | nested | 100,000 | auto | 4,145.2 | 1,719.8 | 299.0 | 0 | 0 | 0 |
| mapping | geometry-header | 100,000 | closedxml | 12,975.5 | 12,035.3 | 420.1 | 0 | 1 | 0 |
| mapping | geometry-header | 100,000 | openxml | 3,910.2 | 1,659.7 | 272.9 | 0 | 0 | 0 |
| mapping | geometry-header | 100,000 | auto | 12,802.1 | 12,042.9 | 423.0 | 0 | 1 | 0 |
| reader | plain | 100,000 | closedxml | 6,869.2 | 1,430.2 | 456.2 | 200,002 | 1 | 0 |
| reader | plain | 100,000 | openxml | 4,901.7 | 1,048.6 | 293.9 | 200,002 | 0 | 0 |
| reader | plain | 100,000 | auto | 5,867.4 | 1,330.8 | 287.0 | 200,002 | 0 | 0 |
| render | plain | 100,000 | closedxml | 11,879.0 | 3,706.1 | 538.8 | 200,002 | 1 | 0 |
| render | plain | 100,000 | openxml | 10,084.1 | 3,329.8 | 358.1 | 200,002 | 0 | 0 |
| mapping-render | plain | 100,000 | closedxml | 16,639.3 | 4,141.2 | 506.1 | 200,002 | 2 | 0 |
| mapping-render | plain | 100,000 | openxml | 13,171.0 | 4,983.4 | 365.7 | 200,002 | 0 | 0 |

The plain/nested OpenXml mapper does not consistently improve time or cumulative allocations. In the 100,000-row plain case it allocates more than the optimized compatibility executor; geometry-header improves substantially. No speed ratio is a CI acceptance threshold.

## Supplemental repeated merges

Original all-column font default, one merge per detail row, 10,000 rows; these results are separate from the bounded matrix.

| Backend | ms | Alloc MiB | Peak RSS MiB | XLWorkbook |
| --- | ---: | ---: | ---: | ---: |
| closedxml | 16,126.7 | 16,511.3 | 238.5 | 1 |
| openxml | 2,337.8 | 203.2 | 202.9 | 0 |
| auto | 16,968.0 | 16,524.7 | 238.6 | 1 |
