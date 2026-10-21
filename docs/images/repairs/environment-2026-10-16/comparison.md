# Matched route evidence

Read-only comparison; all time deltas are candidate minus baseline.

Frame statistics are recomputed from raw positive wall-clock samples using the harness's nearest-rank convention. Each leg covers at most20s and starts at that leg's beginning; counts and movement speed can differ. These are neither full-route/cold-load measurements nor an overall FPS claim. Lighting/exposure differences are reported as candidate behavior, not silently treated as matched. A launch/process roster and unedited images still require human review; JSON alone cannot prove an isolated GPU or visual quality.

## High / day

Technical completion: True → True; elapsed 162.697 → 168.336s. Ready checkpoints 6/6 → 6/6. Grounded trajectory samples 254/254 → 253/254. Same planned route: True.

Graphics differences: `{}`.
Other run-setting differences: `{}`.

| Checkpoint | Player Δm | Camera Δm | View angle Δ° | Solar-hour Δ | Sun elevation Δ° | Exposure Δ |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 00-beach | 0.000 | 0.000 | 0.001 | 0.000000 | 0.000 | 0.000 |
| 01-woodland | 0.000 | 0.000 | 0.000 | -0.000001 | -0.000 | 0.000 |
| 02-clearing | 0.030 | 0.030 | 0.038 | 0.000120 | 0.001 | -0.000 |
| 03-water | 0.075 | 0.075 | 0.120 | -0.000200 | -0.002 | 0.000 |
| 04-cliff | 0.045 | 0.045 | 0.034 | -0.000020 | -0.001 | 0.000 |
| 05-monument | 0.033 | 0.033 | 0.046 | -0.000029 | -0.000 | 0.000 |

| Leg | Raw valid n (base → candidate) | Sample seconds | Median ms | P95 ms | P99 ms | Worst ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| spawn-to-beach | 81 → 70 | 1.267 → 1.251 | 15.582 → 18.094 | 16.781 → 19.218 | 32.360 → 35.685 | 32.360 → 35.685 |
| woodland | 542 → 478 | 8.445 → 8.459 | 15.504 → 17.680 | 16.729 → 19.893 | 17.712 → 20.808 | 29.572 → 34.222 |
| clearing | 1291 → 1174 | 19.994 → 19.999 | 15.201 → 17.058 | 17.694 → 18.736 | 19.053 → 19.734 | 25.991 → 29.453 |
| water | 488 → 456 | 7.670 → 7.641 | 15.407 → 16.778 | 17.385 → 18.657 | 25.892 → 20.172 | 28.783 → 27.290 |
| cliff | 1346 → 1273 | 19.990 → 19.993 | 14.719 → 15.650 | 16.584 → 17.576 | 17.412 → 18.407 | 24.883 → 28.596 |
| monument | 1047 → 979 | 14.896 → 14.895 | 14.248 → 15.192 | 16.107 → 16.953 | 17.671 → 17.812 | 25.035 → 24.951 |

Raw sample validation: 0 invalid samples; reported statistics agree for 12/12 profiles.

VRAM telemetry: not recorded; unavailable, not zero.

## Maximum / night

Technical completion: True → True; elapsed 164.490 → 167.685s. Ready checkpoints 6/6 → 6/6. Grounded trajectory samples 250/251 → 250/251. Same planned route: True.

Graphics differences: `{}`.
Other run-setting differences: `{}`.

| Checkpoint | Player Δm | Camera Δm | View angle Δ° | Solar-hour Δ | Sun elevation Δ° | Exposure Δ |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 00-beach | 0.000 | 0.000 | 0.000 | 0.000000 | -0.000 | 0.000 |
| 01-woodland | 0.002 | 0.002 | 0.001 | 0.000006 | 0.000 | 0.008 |
| 02-clearing | 0.001 | 0.001 | 0.002 | 0.000006 | -0.000 | 0.001 |
| 03-water | 0.001 | 0.001 | 0.002 | 0.000003 | 0.000 | 0.001 |
| 04-cliff | 0.000 | 0.000 | 0.000 | 0.000001 | 0.000 | 0.000 |
| 05-monument | 0.067 | 0.068 | 0.088 | 0.000025 | 0.003 | 0.128 |

| Leg | Raw valid n (base → candidate) | Sample seconds | Median ms | P95 ms | P99 ms | Worst ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| spawn-to-beach | 42 → 35 | 1.257 → 1.249 | 29.274 → 35.428 | 33.170 → 38.694 | 86.604 → 103.880 | 86.604 → 103.880 |
| woodland | 297 → 232 | 8.427 → 8.462 | 28.043 → 36.200 | 32.022 → 40.619 | 34.284 → 42.198 | 39.817 → 49.568 |
| clearing | 715 → 572 | 19.991 → 19.982 | 27.380 → 34.200 | 32.867 → 39.548 | 35.620 → 41.129 | 39.652 → 45.501 |
| water | 254 → 277 | 7.674 → 7.685 | 29.903 → 27.787 | 35.966 → 31.121 | 38.073 → 32.956 | 40.137 → 36.641 |
| cliff | 685 → 733 | 19.975 → 19.983 | 29.252 → 26.392 | 34.354 → 32.649 | 42.245 → 35.362 | 69.296 → 37.135 |
| monument | 604 → 541 | 14.914 → 14.918 | 24.616 → 26.439 | 27.464 → 32.654 | 28.501 → 35.351 | 35.313 → 39.112 |

Raw sample validation: 0 invalid samples; reported statistics agree for 12/12 profiles.

VRAM telemetry: not recorded; unavailable, not zero.
