# Backend-neutral accessor (#221)

The generic accessor is deferred from 0.1. Its shape depends on the column catalog (#220), the final
builder (#217), and row-group metadata selection (#222). Shipping an interface now would either expose
unstable generated methods or require a breaking reshaping immediately at the 0.1 freeze.

The follow-up must provide a single backend-neutral interface in the Attributes package, a generated
zero-allocation singleton implementation for both emitters, and a generic consumer test covering
modern and classic Parquet.Net. The interface must remain reflection-free and Native AOT safe.

This closes the 0.1 scope question for #221; implementation remains a post-freeze follow-up.
