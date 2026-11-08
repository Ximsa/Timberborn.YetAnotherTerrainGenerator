# Face-Lookup WFC for Binary 3D Maps (v2)

Replaces the coarse-block overlapping WFC (see `terrain-generators-plan.md`, Phase 4). Code: `Core/Wfc`
(`WfcPatternShape`, `WfcPatternIndex`, `WfcSolver`, `WfcTerrainGenerator`), dialog in `Game/Wfc`.

## Status (2026-11-08)

| Step | State |
|---|---|
| 1 Encoding, rotation, faces | Done; `TerrainPreview wfc-test` checks four turns = identity, canonical form, and shared faces of source windows 3 apart (also turned) |
| 2 Training, measurements (§3.5) | Done; `Describe()` prints them (preview tool, and `Player.log` in game) |
| 3 Face index | Done |
| 4 Generation, repair, restart | Done, plus trap clearing (see decisions) |
| 5 Lookahead | Done, on by default; compared below |
| 6 Novelty, vertical profile | Done in the preview tool; height weights added (see decisions) |
| 7 Multi-level tables (§12) | Not started; candidate lookup is not the bottleneck at 128 x 128 |
| 8 Cell selection order (§13) | Not started |
| Pattern size setting | Done: width 4-10 across, height 4-6 up in the dialog, 6 x 6 x 4 by default (2026-11-08); `wfc-test` checks 4x4x4, 6x6x4, 8x8x5, 9x9x6, 10x10x6 |
| In game | 4 x 4 x 4 tested with 10 maps (solved, 3.6 s); one small map alone failed and raised the game's error report, now a message box instead. Pattern size sliders and the message box not yet tested in game |

## Decisions and deviations from the spec

| Topic | Decision | Why |
|---|---|---|
| Axes | Code uses the game's frame: x, y across, z up. Spec Y = code z. Bits `x + 4y + 16z`; directions +x, -x, +y, -y, +z (up), -z; faces x: `4z + y`, y: `4z + x`, z: `4y + x`; turn `(x, y, z) -> (3 - y, x, z)` | Matches `VoxelMap` |
| Domain formula | `domain(c) = ∩ cand(d, face(assigned[n], opp(d)))` for `n = c + δ(d)` | §5.1 as written swaps the two directions; §5.3's lookahead formula is the consistent one |
| Lookahead cap | Candidates are always tested in weighted-random order and the first that passes wins | Same odds as filtering the whole domain, usually one test; no cap needed |
| Map sizes | Any size: cells per axis `ceil((size - 1) / 3)`, cropped. Bottom voxel layer is forced solid | The game's maps vary in size; the old generator also kept the floor |
| Trap clearing | When the same cell needs its 8th repair (`ClearEvery`), all placements in a box of radius `repairs / 8` (at most 3) around it are removed and their blacklists dropped | At 256 x 256 the pure §5.5 policy failed 4 of 5 attempts on seed 1: one cell was repaired 1,093 times. Its domain was not empty, but every candidate emptied a free neighbour, which removing its own neighbours did not fix, while blacklists only grew. With clearing, every tested seed solved on the first attempt |
| Height weights | Each pattern counts its occurrences per height band (window start z rounded to a cell layer, `round(z / 3)`). Weight at cell layer L = Σ count(band) · penalty^|L − band| / distinct rotations. Default penalty 0.25; 1 gives the spec's height-free weights. Dialog slider | Requested, as in the old generator. Without it, terrain in 23-high maps grows far above the sources' top (sources end at z = 15) |
| Pattern size | Width W across, height H up (W ≤ 10, W·H ≤ 64; each layer in one or two 64-bit words, all patterns in one pool with an open-addressing table, `WfcPatternStore`); cells W − 1 apart across and H − 1 up; height bands H − 1 voxels. Faces are interned per direction in dictionaries (no dense 16-bit tables); each oriented id stores the slot of its neighbour's candidate list per direction; weights are kept per pattern, codes only in rotation 0. No pruning of rare patterns | Requested to compare 4x4x4 with wider patterns; pruning explicitly not wanted |
| Failure | After `MaxAttempts` (30) the generator throws `GenerationFailedException`; `TerrainJobDialog` logs a warning, shows the reason in the progress bar and the WFC dialog shows a message box, without the game's error report | A single small source map failed all attempts in game and the logged exception opened the error report |
| Tunables | `MaxDeletions` 2000, `MaxAttempts` 30, `Lookahead` on, `ClearEvery` 8, `HeightPenalty` 0.25 | Measured below |

## Measurements (offline, all 22 extracted built-in maps)

- Training: 17.1 M windows, 142,871 patterns, 570,972 oriented ids (rotations 1/2/4: 26 / 217 / 142,628),
  about 3,900 face values per horizontal table (longest list 46k), 4,200-4,700 vertically (longest 163k),
  index about 30 MB, learning about 9 s.
- 128 x 128 x 16, penalty 1, no clearing, 8 seeds: all solved in one attempt, about 1.4 s each, novelty 4-10%.
  With lookahead: 0-488 repairs (median 4). Without: 22-619 (median about 80), same speed.
- 256 x 256 x 23, seeds 1-4: pure repair 9-24 s, 5 failed attempts in total; with clearing all in one attempt, about 9 s.
- Fill per height, 128 x 128 x 23 (sources / penalty 1 / 0.5 / 0.25): z 8: 32 / 61 / 64 / 58%;
  z 15: 4 / 30 / 24 / 16%; z 19: 0 / 13 / 8 / 3%; z 22: 0 / 4 / 2 / 0%. The middle heights stay about twice as full
  as the sources; hard layer bands (one face index per band, §6) are the next step if that matters.
- Pattern sizes, 128 x 128 x 23, seeds 1-4, penalty 0.25 (novelty is measured on windows of each size, so it is not
  comparable across sizes):

  | Shape | Patterns | Oriented | Side / top face values | Learning | Solve per map | Peak memory |
  |---|---|---|---|---|---|---|
  | 4x4x4 | 142,871 | 570,972 | 3,904 / 4,163 | 4.6 s | 2.2-2.4 s | 202 MB |
  | 5x5x4 | 451,138 | 1,803,622 | 9,853 / 25,241 | 6.2 s | 3.4-4.4 s | 404 MB |
  | 6x6x4 | 955,142 | 3,819,516 | 20,269 / 99,421 | 10.8 s | 3.8-5.9 s | 933 MB |

  All solved on the first attempt. Wider patterns give broader plateaus and longer cliff lines.
- After the move to variable-length patterns (same 22 maps): 6x6x4 gives identical maps, learning 15.7 s, peak 908 MB;
  8x8x5: 2.7 M patterns, learning 80 s, peak 2.0 GB, solves 4.6-13 s. Large sizes are meant for few source maps:
  10x10x6 from Diorama + Cliffside learns in 0.8 s (36k patterns, 11 MB) and solved on the 8th attempt, about 7 s.
  Selecting many maps with large patterns is a user error (memory), not guarded.

---

The specification follows as given (v2).

## 1. Goal

Generate a binary voxel map of 128 × 128 × 16 (X × Z × Y; Y is up) from ~20 example maps of the same size. Voxel
values: `0` = air, `1` = block.

The generator is a WFC-style constraint solver over 4 × 4 × 4 patterns. Adjacent pattern placements **share one voxel
layer** (stride 3), so compatibility between neighbours is exact equality of their shared face.

Two phases:

1. **Training** — extract, deduplicate and index patterns. The index is read-only afterwards.
2. **Generation** — place patterns cell by cell. When a cell has no valid candidate, un-place one of its neighbours
   and blacklist that choice; restart if too many deletions are needed.

## 2. Conventions (must be identical in training and generation)

### 2.1 Coordinates and pattern encoding

A pattern is a 4 × 4 × 4 cube with local coordinates `x, y, z ∈ 0..3`, encoded as a 64-bit integer with
`bit(x, y, z) = x + 4·z + 16·y`. Each horizontal Y-layer is one contiguous 16-bit chunk.

### 2.2 Directions

| d | name | face (local)  |
|---|------|---------------|
| 0 | +X   | x = 3         |
| 1 | −X   | x = 0         |
| 2 | +Y   | y = 3 (top)   |
| 3 | −Y   | y = 0 (bottom)|
| 4 | +Z   | z = 3         |
| 5 | −Z   | z = 0         |

`opp(d) = d XOR 1`.

### 2.3 Face encoding (world frame)

```
±X face: bit index = 4·y + z
±Y face: bit index = 4·z + x
±Z face: bit index = 4·y + x
```

Two neighbours are compatible exactly when `face(A, d) == face(B, opp(d))` where `B = A + δ(d)`. No mirroring is
needed when matching; a rotated pattern's faces are recomputed from rotated voxels once per pattern during training.

### 2.4 Rotation

Only rotations about +Y, no mirroring. R1 maps `(x, y, z) → (3 − z, y, x)`; `Rr` is R1 applied r times.

## 3. Training

### 3.1 Extraction

Slide a 4 × 4 × 4 window over every position of every source map, stride 1, no wrapping. Every window is a valid
pattern, even though output placements use stride 3. Patterns are not tagged with height (but see the height weights
above).

### 3.2 Deduplication up to rotation

For each window `v`: `canon(v) = min_r encode(Rr(v))`; look it up in a hash map keyed by the 64-bit value. A new
pattern gets the next `PatternId p`, `voxels[p] = canon(v)`, its distinct orientations `Rot(p)` (1, 2 or 4) and
`face[p][r][d]` for every orientation and direction. Increment `freq[p]`.

`OrientedId = 4·p + r` (only `r ∈ Rot(p)` valid).

### 3.3 Weights

`w(p, r) = freq[p] / |Rot(p)|`.

### 3.4 Face index

Six tables, one per direction, each a dense CSR array from a 16-bit face value to the sorted list of oriented IDs with
that face on side `d`: `cand(d, v) = ids[d][offsets[d][v] .. offsets[d][v+1]]`. No multi-face index: intersect the
single-face lists, shortest first.

### 3.5 Measurements to log

Raw windows; unique classes `P`; oriented IDs `M`; symmetry histogram; per table distinct face values, mean and
maximum list length; total index memory.

## 4. Output grid

Cells on a stride-3 lattice: 43 (X) × 5 (Y) × 43 (Z). Cell `(cx, cy, cz)` covers voxels `[3cx .. 3cx+3]` etc.
Vertical extent 3·5 + 1 = 16; horizontal 3·43 + 1 = 130, cropped to 128. Neighbouring cells share one voxel layer.
All map edges are open: a face on the outside of the grid has no constraint.

## 5. Generation

### 5.1 State

```
assigned[cell]    OrientedId or NONE
blacklist[cell]   set of OrientedIds banned at this cell (cleared on restart)
domain[cell]      cached candidate list for unassigned cells, or FULL
deletions         counter for this attempt
```

A domain is a pure function of the cell's assigned neighbours and its blacklist (intersection of the candidate lists
for each assigned neighbour's shared face, minus the blacklist; FULL without assigned neighbours). Removing a
placement only means recomputing the domains next to it; no undo log.

### 5.2 Main loop

```
attempt:
    clear all state; deletions ← 0
    seed: pick a random cell; place a weighted-random candidate
    loop:
        if every cell is assigned: done → materialise (§5.6)
        if some unassigned cell has an empty domain: repair (§5.5)
        else:
            c ← unassigned, non-FULL cell with the smallest |domain| (ties random)
            place(c)
```

### 5.3 Placement with one-step lookahead

Keep candidate `o` only if, for every unassigned neighbour `n = c + δ(d)`,
`domain(n) ∩ cand(opp(d), face(o, d))` is non-empty. If none is left, treat `c` as a contradiction (§5.5). Otherwise
pick weighted by `w(o)` and recompute the domains of c's unassigned neighbours.

### 5.4 Contradiction

An unassigned cell with an empty domain, or one whose candidates were all rejected by the lookahead.

### 5.5 Repair by neighbour removal

```
repair(c):
    if deletions ≥ D_max: restart attempt
    N ← assigned neighbours of c
    gain(n) ← |domain of c recomputed without n's constraint|
    choose n*: the largest gain > 0 (ties random); if every gain is 0, a random n
    blacklist[n*].add(assigned[n*]); assigned[n*] ← NONE; deletions += 1
    recompute domain(n*), domain(c) and the domains of n*'s other unassigned neighbours
```

`D_max = 2,000` deletions per attempt to start with; restarts unbounded or capped.

### 5.6 Materialisation

Write `R_r(voxels[p])` into each cell's box; shared layers are written twice and must agree. Crop.

## 6. Validation and metrics

- Every shared face is equal (hard check).
- Novelty: fraction of output 4 × 4 × 4 windows not in the training dictionary.
- Vertical profile: fill ratio per height against the training average. If it drifts, layer bands can be added: tag
  each occurrence with its height layer and build one face index per layer.
- Per attempt: placements, repairs, deletions, restarts, lookahead rejections.

## 7. Complexity

Training O(windows · 4) plus O(M · 6) for indexing; domain recomputation up to 6 sorted-list intersections, only for
neighbours of a changed cell; generation memory bounded by the grid size.

## 8. Resolved decisions

| # | Decision | Choice |
|---|----------|--------|
| Q1 | How patterns depend on height | None; any pattern at any height (check the vertical profile, §6) — since softened by height weights, above |
| Q2 | Horizontal map edges | Open; generate 130 wide and crop to 128 |
| Q3 | Frequency weight across rotations | Split evenly across distinct orientations |
| Q4 | Contradiction policy | No backtracking; remove one chosen neighbour, blacklist it at that cell, bound deletions, then restart — plus trap clearing, above |

## 9. Implementation order

1. Encoding, rotation and face extraction, with tests.
2. Training plus the measurements in §3.5.
3. Index build.
4. Generation: placement without lookahead, plus repair and restart.
5. Lookahead; compare repair counts with and without it.
6. Novelty and vertical-profile metrics; visual inspection.
7. Experiment: multi-level lookup tables (§12).
8. Experiment: cell selection order (§13).

## 12. Experiment: multi-level lookup tables

Hypothesis: precomputed tables for combinations of 2–6 faces make domain recomputation faster than intersecting the
level-1 lists. Variants: A baseline (level 1 only), B level 2 eager, C level 2 lazy cache, D all levels eager.
Measure build time, memory per level, time per domain recomputation (median, 99th percentile), total time per map,
query statistics; outputs must be identical across variants for the same seed. Adopt a variant over A only if it
cuts total generation time by at least 20% and its memory fits.

## 13. Experiment: cell selection order

Variants: S0 grow from seed (breadth-first), S1 fewest candidates (baseline), S2 weighted entropy, S3 most placed
neighbours (ties by S1), S4 fewest candidates after lookahead. Measure success rate, restarts, repairs, deletions,
time, novelty, vertical profile and pattern usage against training frequencies. Prefer the highest success rate,
then quality, then speed; keep S1 unless another is clearly better.
