# Write builder symmetry (#219)

The symmetric write builder is deferred from 0.1. The current four write entry points are stable,
covered by generated API baselines, and do not block the 0.1 safety, compatibility, or AOT claims.

A write builder should be designed alongside the final read builder and the column/accessor seams,
otherwise batching and source-shape decisions become a second migration. The post-0.1 design must
settle whether the existing collection methods remain the front door, then express collection shape,
batching, and async source as builder state. It must preserve both emitters and Native AOT coverage.

This closes the 0.1 scope question for #219; implementation remains a post-freeze follow-up.
