# Actual work counters

These are separate diagnostic runs; their times are excluded from performance results.
The before counter build is an isolated observation-only patch of the saved source.
The unique-candidate/population/evaluation budgets retain their original meanings.
Rule-list work counts per-cell preparation before versus shared band-plan preparation after.

| Work | Before | After |
| --- | ---: | ---: |
| 100-page PDF build events | 300 | 200 |
| 100-page PDF null surfaces | 100 | 0 |
| 10-page PS build events | 30 | 20 |
| Repeated-image PS alpha scans | 20 | 1 |
| Repeated-image PS alpha cache hits | 0 | 19 |
| 100k CF display visits | 400000 | 100000 |
| 100k CF unique candidates | 100000 | 100000 |
| 100k CF application-rule index queries | 100000 | 1 |
| 100k CF cell ordered-list/band-plan constructions | 100000 | 1 |
| Mapping application-rule index queries | 10000 | 1 |
| Mapping cell ordered-list/band-plan constructions | 10000 | 1 |
| Disabled CF initial priority sorts | 1 | 0 |

Actual shape-text PNG legacy painting: {'before': 5, 'after': 0} (five supplied shape texts).
