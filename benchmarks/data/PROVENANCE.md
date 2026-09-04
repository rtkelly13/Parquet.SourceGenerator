# Benchmark Dataset Provenance & Integrity Register

This directory contains source-controlled Parquet datasets managed via **Git LFS** for reliable, deterministic, and offline-capable continuous integration (CI) and performance benchmarking.

## 📦 Registered Datasets

| Dataset | File | Rows | Size | Codec | Dict Columns | Upstream Repo | License | SHA-256 |
|:---|:---|:---:|:---:|:---:|:---:|:---|:---:|:---:|
| **tpch-lineitem-sf001** | `tpch_lineitem_sf001.parquet` | 60,175 | 1312.9 KB | `zstd` | 13/16 | [liangyc/tpch-sf-0_01](https://huggingface.co/datasets/liangyc/tpch-sf-0_01) | `Apache-2.0` | `c2a3d37cff20...` |
| **adult-census-income** | `adult_census_income.parquet` | 32,561 | 540.8 KB | `snappy` | 15/15 | [scikit-learn/adult-census-income](https://huggingface.co/datasets/scikit-learn/adult-census-income) | `CC-BY-4.0` | `5a285f7b7323...` |
| **diamonds** | `diamonds.parquet` | 53,940 | 767.2 KB | `snappy` | 10/10 | [inria-soda/tabular-benchmark](https://huggingface.co/datasets/inria-soda/tabular-benchmark) | `CC0-1.0` | `828f91f368b7...` |

## 🔍 Schema & Encoding Breakdown

### tpch-lineitem-sf001 (`tpch_lineitem_sf001.parquet`)

- **Description**: TPC-H Benchmark lineitem table at Scale Factor 0.01 (standard analytical decision support benchmark).
- **Upstream Source**: [liangyc/tpch-sf-0_01 (commit `a91e9442ea`)](https://huggingface.co/datasets/liangyc/tpch-sf-0_01)
- **Download URL**: `https://huggingface.co/datasets/liangyc/tpch-sf-0_01/resolve/a91e9442ea9dc1fe4729db3138afdd03ef36a9e5/lineitem/train/0000.parquet`
- **License**: `Apache-2.0`
- **File Size**: 1,344,366 bytes
- **SHA-256**: `c2a3d37cff204e6569e35fa63f0efe0c89d43a36ac2dd25b8e65f90a1e9b0ccc`
- **Row Count**: 60,175
- **Row Groups**: 1
- **Compression**: `zstd`

| Column Name | Physical Type | Logical Type | Encoded via Dictionary? |
|:---|:---|:---|:---:|
| `l_orderkey` | `INT64` | `Int(bitWidth=64, isSigned=true)` | ❌ Plain |
| `l_partkey` | `INT64` | `Int(bitWidth=64, isSigned=true)` | ✅ Yes |
| `l_suppkey` | `INT64` | `Int(bitWidth=64, isSigned=true)` | ✅ Yes |
| `l_linenumber` | `INT64` | `Int(bitWidth=64, isSigned=true)` | ✅ Yes |
| `l_quantity` | `INT64` | `Decimal(precision=15, scale=2)` | ✅ Yes |
| `l_extendedprice` | `INT64` | `Decimal(precision=15, scale=2)` | ❌ Plain |
| `l_discount` | `INT64` | `Decimal(precision=15, scale=2)` | ✅ Yes |
| `l_tax` | `INT64` | `Decimal(precision=15, scale=2)` | ✅ Yes |
| `l_returnflag` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `l_linestatus` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `l_shipdate` | `INT32` | `Date` | ✅ Yes |
| `l_commitdate` | `INT32` | `Date` | ✅ Yes |
| `l_receiptdate` | `INT32` | `Date` | ✅ Yes |
| `l_shipinstruct` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `l_shipmode` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `l_comment` | `BYTE_ARRAY` | `String` | ❌ Plain |

### adult-census-income (`adult_census_income.parquet`)

- **Description**: Adult Census Income dataset from UCI Machine Learning Repository (standard categorical tabular benchmark).
- **Upstream Source**: [scikit-learn/adult-census-income (commit `aefa0f0f1b`)](https://huggingface.co/datasets/scikit-learn/adult-census-income)
- **Download URL**: `https://huggingface.co/datasets/scikit-learn/adult-census-income/resolve/aefa0f0f1b03a11dd48f460913e20a1d50b4e53c/default/train/0000.parquet`
- **License**: `CC-BY-4.0`
- **File Size**: 553,790 bytes
- **SHA-256**: `5a285f7b73234dda6fb69ea8bbd2655e850a3d9efd8c81512785afb1f7773517`
- **Row Count**: 32,561
- **Row Groups**: 33
- **Compression**: `snappy`

| Column Name | Physical Type | Logical Type | Encoded via Dictionary? |
|:---|:---|:---|:---:|
| `age` | `INT64` | `None` | ✅ Yes |
| `workclass` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `fnlwgt` | `INT64` | `None` | ✅ Yes |
| `education` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `education.num` | `INT64` | `None` | ✅ Yes |
| `marital.status` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `occupation` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `relationship` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `race` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `sex` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `capital.gain` | `INT64` | `None` | ✅ Yes |
| `capital.loss` | `INT64` | `None` | ✅ Yes |
| `hours.per.week` | `INT64` | `None` | ✅ Yes |
| `native.country` | `BYTE_ARRAY` | `String` | ✅ Yes |
| `income` | `BYTE_ARRAY` | `String` | ✅ Yes |

### diamonds (`diamonds.parquet`)

- **Description**: Diamonds dataset from ggplot2 (standard regression tabular benchmark with numeric & ordinal features).
- **Upstream Source**: [inria-soda/tabular-benchmark (commit `cb2bcee34f`)](https://huggingface.co/datasets/inria-soda/tabular-benchmark)
- **Download URL**: `https://huggingface.co/datasets/inria-soda/tabular-benchmark/resolve/cb2bcee34f8fbd271c04dc644fd91f23a51a8570/reg_cat_diamonds/train/0000.parquet`
- **License**: `CC0-1.0`
- **File Size**: 785,608 bytes
- **SHA-256**: `828f91f368b79d520b200c393989e820adb7cbda7545fdf66b8552972467789e`
- **Row Count**: 53,940
- **Row Groups**: 54
- **Compression**: `snappy`

| Column Name | Physical Type | Logical Type | Encoded via Dictionary? |
|:---|:---|:---|:---:|
| `carat` | `DOUBLE` | `None` | ✅ Yes |
| `cut` | `INT64` | `None` | ✅ Yes |
| `color` | `INT64` | `None` | ✅ Yes |
| `clarity` | `INT64` | `None` | ✅ Yes |
| `depth` | `DOUBLE` | `None` | ✅ Yes |
| `table` | `DOUBLE` | `None` | ✅ Yes |
| `x` | `DOUBLE` | `None` | ✅ Yes |
| `y` | `DOUBLE` | `None` | ✅ Yes |
| `z` | `DOUBLE` | `None` | ✅ Yes |
| `price` | `DOUBLE` | `None` | ✅ Yes |

