# Row-group metadata and selection (#222)

Public row-group enumeration and explicit subset reads are deferred from 0.1. Existing generated
pruning remains internal and continues to provide predicate pushdown and sorted-key behavior. Exposing
metadata before the catalog, accessor, and builder contracts settle would duplicate their addressing
and predicate semantics.

The follow-up must provide allocation-free metadata enumeration, explicit ranges/subsets, uniform
predicate availability, and tests proving skipped row groups perform no I/O. It must refactor the
existing pruning components onto the shared mechanism and cover both emitters.

This closes the 0.1 scope question for #222; implementation remains a post-freeze follow-up.
