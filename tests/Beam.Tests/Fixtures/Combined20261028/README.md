Five detached beam/slab areas from the 2026-10-28 diagnostic. Project identity,
original element IDs and global Revit coordinates are omitted. Local outlines,
unequal beam spans and the reported load/stock settings are retained for solver
reproduction. These are regression inputs, not site-approved designs.

`--combined-boundaries` checks the member-end rounding regression and fixture loading.
`--combined-reported --areas 1,2,3,4,5 --output <directory>` benchmarks full searches.
Optional `--seconds N` cancels a benchmark explicitly; it does not change production
search limits. `--profile` enables sampled timings; `--workers N` controls benchmark
parallelism. `--first` is a diagnostic feasibility comparison only.

`--area-workers 2 --workers 4` benchmarks two areas with two numerical workers
each. This is an experiment in the test harness; production keeps its current
area scheduling until a reproducible benefit and identical designs are verified.
