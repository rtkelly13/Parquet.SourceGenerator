# Alternative logo design — hexagon and circuit mark

Long-lived branch holding the logo direction **not** adopted on `main`. Nothing here ships; it
exists so the work is recoverable and the reasoning is not lost.

`main` uses the interlocking-columns mark. See
[`docs/assets/STATEMENT_OF_INTENT.md`](../STATEMENT_OF_INTENT.md) there.

## What this direction was

A rhombille (or herringbone) tiling inside a glowing hexagon frame, with a seam-locked circuit
network, a notched socket dot and etched `{}` / `</>` emblems. It was reconstructed by measurement
from [`hexagon-reference.jpg`](./hexagon-reference.jpg) — the original generated raster artwork,
byte-identical to the copy packaged inside the builder.

## Building it

The builder is preserved in the shared-utilities repo and still works:

```bash
uv run image-reconcile build \
    -B src/shared_utilities/image_drift/rhombille_builder.py \
    --variant rhombille_reference --svg /tmp/hexagon.svg

uv run image-reconcile build -B .../rhombille_builder.py --list-variants
```

Variants: `rhombille_reference`, `rhombille_hairline`, `rhombille_bold_trace`,
`rhombille_soft_glow`, `rhombille_amber`, `rhombille_fine`, `herringbone_warm` (the literal parquet
weave), and `favicon`.

It shares every geometry helper with the adopted mark, so fixes to the common parts reach both.

## Why it was not adopted

Measured against the standard logo tests, the mark had problems that were structural rather than
cosmetic:

| Test | Result |
| :--- | :--- |
| 1-bit threshold | Collapsed to a wireframe box — no usable silhouette |
| Greyscale | Circuit traces dropped to nearly the same value as the wood; hue carried all separation |
| Squint | Read as a brown hexagon |
| Colour mass | Wood 21% vs circuit 8.1% — the meaningless element outweighed the meaningful one 2.6:1 |
| 16–32px | Interior collapsed to a beige blob |

It also read as an isometric **box** rather than as parquet — a crowded visual space (packages,
containers) and semantically wrong for a serializer. The parquet-floor pun the project name rests on
was lost once the tiling moved from herringbone to rhombille.

## What was worth keeping

The socket detail — a dot seated in a notched ring on the frame — was the one genuinely distinctive
element, and it carried into the adopted mark as a void with an island dot. Its geometry was derived
by measurement rather than by eye: frame radius 186.2 fitted per edge from the stroke centreline
(measuring the glow instead reads ~188, because the bloom extends outward), wood radius 173, and the
dot projected onto the frame edge so it always sits exactly on the line.
