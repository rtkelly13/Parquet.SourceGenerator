# Feature profiles and per-type overrides (#225)

Named feature profiles are deferred from 0.1. Issue #290 now provides the configuration channel and
feature-level policy, but the profile matrix still depends on the final generated builder and the
stable emitted API freeze.

The post-freeze design will support named profiles rather than arbitrary independent bits, document
the MSBuild-versus-attribute precedence, and test each profile on both emitters. `NetStandardCompat`
must be demonstrated by a real downstream net472/netstandard2.0 consumer before it is advertised.

This closes the 0.1 scope question for #225; implementation remains a post-freeze follow-up.
