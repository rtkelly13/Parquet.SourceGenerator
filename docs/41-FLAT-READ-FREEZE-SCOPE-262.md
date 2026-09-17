# Flat-read API freeze (#262)

The flat read methods remain in the current pre-0.1 compatibility window. Removing them before the
builder and its replacement seams have had a release would make the first frozen release the first
real-world migration point, and would leave the write side intentionally asymmetric.

The removal gate is therefore post-0.1-window work: #244 must prove parameter-level shrinkage, #219
must settle write symmetry or its explicit deferral, and the final freeze must record the breaking
surface in the ledger. At that point remove the forwarders from both emitters, update D3 in the API
contract, and publish migration guidance (an `[Obsolete]` release is not useful unless a release
exists in which callers can move).

This closes the current 0.1 scope question for #262; implementation remains the next compatibility
window's breaking-change follow-up.
