# Feature-profile golden matrix (#227)

## Decision

The profile-by-model golden matrix is deferred. The repository already gates the default
profile through generated source, API, metrics, call-graph, AOT, and compatibility contracts. Adding
matrix rows before #225 settles the supported profile set would multiply baselines without a stable
configuration contract.

The later matrix must be profile × model × backend, include compile and round-trip checks, verify
`NetStandardCompat` against a real consumer, and report CI wall-clock cost. A single update workflow
must refresh all artifacts atomically.

## Follow-up

Revisit #227 after #225 settles the supported profile set. The matrix must cover profile, model, and
backend combinations, with one workflow that refreshes all artifacts atomically.
