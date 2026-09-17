# LINQ companion package scope (#223)

The LINQ companion package is explicitly outside the 0.1 release. The generator remains the
compile-time physical layer; an expression-tree provider belongs in a separate package only after
the builder, column catalog, accessor, and row-group selection seams are stable.

The follow-up must define an AOT-safe plan core, residual-predicate behavior, `ExplainAsync()`, strict
fallback behavior, and multi-file/partition pruning. It must demonstrate both emitters through the
backend-neutral accessor rather than reaching into generated implementation details.

This closes the 0.1 scope question for #223; implementation remains a post-freeze follow-up.
