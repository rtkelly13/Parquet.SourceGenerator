# Feature-profile golden matrix (#227)

The profile-by-model golden matrix is deferred from 0.1. The repository already gates the default
profile through generated source, API, metrics, call-graph, AOT, and compatibility contracts. Adding
matrix rows before #225 settles the supported profile set would multiply baselines without a stable
configuration contract.

The post-freeze matrix must be profile × model × backend, include compile and round-trip checks, verify
`NetStandardCompat` against a real consumer, and report CI wall-clock cost. A single update workflow
must refresh all artifacts atomically.

This closes the 0.1 scope question for #227; implementation remains a post-freeze follow-up.
