# /// script
# dependencies = [
#     "pyarrow",
#     "requests",
# ]
# ///

"""
Fetch and verify fixed public benchmark datasets from Hugging Face Hub.
Tracks provenance including exact commit SHAs, URLs, licenses, SHA-256 digests,
and Parquet schema profiles for Git LFS.
"""

import hashlib
import json
import os
import sys
from datetime import datetime, timezone
import pyarrow.parquet as pq
import requests

BENCHMARK_DATA_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "benchmarks", "data"))

DATASET_CONFIGS = [
    {
        "id": "tpch-lineitem-sf001",
        "filename": "tpch_lineitem_sf001.parquet",
        "description": "TPC-H Benchmark lineitem table at Scale Factor 0.01 (standard analytical decision support benchmark).",
        "source_platform": "Hugging Face Hub",
        "upstream_repository": "liangyc/tpch-sf-0_01",
        "upstream_commit": "a91e9442ea9dc1fe4729db3138afdd03ef36a9e5",
        "source_url": "https://huggingface.co/datasets/liangyc/tpch-sf-0_01/resolve/a91e9442ea9dc1fe4729db3138afdd03ef36a9e5/lineitem/train/0000.parquet",
        "license": "Apache-2.0"
    },
    {
        "id": "adult-census-income",
        "filename": "adult_census_income.parquet",
        "description": "Adult Census Income dataset from UCI Machine Learning Repository (standard categorical tabular benchmark).",
        "source_platform": "Hugging Face Hub",
        "upstream_repository": "scikit-learn/adult-census-income",
        "upstream_commit": "aefa0f0f1b03a11dd48f460913e20a1d50b4e53c",
        "source_url": "https://huggingface.co/datasets/scikit-learn/adult-census-income/resolve/aefa0f0f1b03a11dd48f460913e20a1d50b4e53c/default/train/0000.parquet",
        "license": "CC-BY-4.0"
    },
    {
        "id": "diamonds",
        "filename": "diamonds.parquet",
        "description": "Diamonds dataset from ggplot2 (standard regression tabular benchmark with numeric & ordinal features).",
        "source_platform": "Hugging Face Hub",
        "upstream_repository": "inria-soda/tabular-benchmark",
        "upstream_commit": "cb2bcee34f8fbd271c04dc644fd91f23a51a8570",
        "source_url": "https://huggingface.co/datasets/inria-soda/tabular-benchmark/resolve/cb2bcee34f8fbd271c04dc644fd91f23a51a8570/reg_cat_diamonds/train/0000.parquet",
        "license": "CC0-1.0"
    }
]


def download_file(url: str, dest_path: str) -> str:
    print(f"📥 Fetching: {url} -> {dest_path}")
    headers = {"User-Agent": "Parquet.SourceGenerator-Benchmark-Sync/1.0"}
    resp = requests.get(url, headers=headers, stream=True, timeout=120)
    resp.raise_for_status()

    hasher = hashlib.sha256()
    total_bytes = 0
    with open(dest_path, "wb") as f:
        for chunk in resp.iter_content(chunk_size=65536):
            if chunk:
                f.write(chunk)
                hasher.update(chunk)
                total_bytes += len(chunk)

    digest = hasher.hexdigest()
    print(f"   Done ({total_bytes:,} bytes, SHA-256: {digest})")
    return digest


def inspect_parquet_file(file_path: str):
    parquet_file = pq.ParquetFile(file_path)
    metadata = parquet_file.metadata
    schema = parquet_file.schema

    num_rows = metadata.num_rows
    num_columns = metadata.num_columns
    num_row_groups = metadata.num_row_groups
    format_version = metadata.format_version

    columns = []
    dict_columns = []
    plain_columns = []

    # Inspect first row group columns
    rg = metadata.row_group(0)
    for c_idx in range(rg.num_columns):
        col_meta = rg.column(c_idx)
        col_name = col_meta.path_in_schema
        physical_type = col_meta.physical_type
        encodings = [str(enc) for enc in col_meta.encodings]
        is_dict = any("RLE_DICTIONARY" in enc or "PLAIN_DICTIONARY" in enc for enc in encodings)
        if is_dict:
            dict_columns.append(col_name)
        else:
            plain_columns.append(col_name)

        col_schema = schema.column(c_idx)
        columns.append({
            "name": col_name,
            "physical_type": str(physical_type),
            "logical_type": str(col_schema.logical_type) if col_schema.logical_type else None,
            "encodings": encodings,
            "dictionary_encoded": is_dict
        })

    compression = rg.column(0).compression if rg.num_columns > 0 else "UNKNOWN"

    return {
        "row_count": num_rows,
        "column_count": num_columns,
        "row_group_count": num_row_groups,
        "format_version": format_version,
        "compression": compression.lower(),
        "columns": columns,
        "dictionary_encoded_columns": dict_columns,
        "plain_columns": plain_columns
    }


def main():
    os.makedirs(BENCHMARK_DATA_DIR, exist_ok=True)
    manifest = {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "description": "Cryptographic Data Provenance Manifest for Parquet.SourceGenerator Benchmarks",
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "datasets": []
    }

    for cfg in DATASET_CONFIGS:
        dest_path = os.path.join(BENCHMARK_DATA_DIR, cfg["filename"])
        
        # Download file
        sha256 = download_file(cfg["source_url"], dest_path)
        file_size = os.path.getsize(dest_path)

        # Inspect parquet profile
        profile = inspect_parquet_file(dest_path)

        entry = {
            "id": cfg["id"],
            "filename": cfg["filename"],
            "description": cfg["description"],
            "provenance": {
                "source_platform": cfg["source_platform"],
                "upstream_repository": cfg["upstream_repository"],
                "upstream_commit": cfg["upstream_commit"],
                "source_url": cfg["source_url"],
                "license": cfg["license"],
                "verified_at": datetime.now(timezone.utc).isoformat()
            },
            "integrity": {
                "file_size_bytes": file_size,
                "sha256": sha256,
                "git_lfs_oid": f"sha256:{sha256}"
            },
            "parquet_profile": profile
        }
        manifest["datasets"].append(entry)

    manifest_path = os.path.join(BENCHMARK_DATA_DIR, "provenance.json")
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    print(f"\n📄 Saved provenance manifest to {manifest_path}")

    # Generate Markdown summary table
    md_path = os.path.join(BENCHMARK_DATA_DIR, "PROVENANCE.md")
    with open(md_path, "w", encoding="utf-8") as f:
        f.write("# Benchmark Dataset Provenance & Integrity Register\n\n")
        f.write("This directory contains source-controlled Parquet datasets managed via **Git LFS** for reliable, deterministic, and offline-capable continuous integration (CI) and performance benchmarking.\n\n")
        f.write("## 📦 Registered Datasets\n\n")
        f.write("| Dataset | File | Rows | Size | Codec | Dict Columns | Upstream Repo | License | SHA-256 |\n")
        f.write("|:---|:---|:---:|:---:|:---:|:---:|:---|:---:|:---:|\n")
        for ds in manifest["datasets"]:
            prof = ds["parquet_profile"]
            integ = ds["integrity"]
            prov = ds["provenance"]
            size_kb = integ["file_size_bytes"] / 1024.0
            dict_count = len(prof["dictionary_encoded_columns"])
            total_cols = prof["column_count"]
            short_sha = integ["sha256"][:12] + "..."
            f.write(f"| **{ds['id']}** | `{ds['filename']}` | {prof['row_count']:,} | {size_kb:.1f} KB | `{prof['compression']}` | {dict_count}/{total_cols} | [{prov['upstream_repository']}](https://huggingface.co/datasets/{prov['upstream_repository']}) | `{prov['license']}` | `{short_sha}` |\n")
        
        f.write("\n## 🔍 Schema & Encoding Breakdown\n\n")
        for ds in manifest["datasets"]:
            prof = ds["parquet_profile"]
            integ = ds["integrity"]
            prov = ds["provenance"]
            f.write(f"### {ds['id']} (`{ds['filename']}`)\n\n")
            f.write(f"- **Description**: {ds['description']}\n")
            f.write(f"- **Upstream Source**: [{prov['upstream_repository']} (commit `{prov['upstream_commit'][:10]}`)](https://huggingface.co/datasets/{prov['upstream_repository']})\n")
            f.write(f"- **Download URL**: `{prov['source_url']}`\n")
            f.write(f"- **License**: `{prov['license']}`\n")
            f.write(f"- **File Size**: {integ['file_size_bytes']:,} bytes\n")
            f.write(f"- **SHA-256**: `{integ['sha256']}`\n")
            f.write(f"- **Row Count**: {prof['row_count']:,}\n")
            f.write(f"- **Row Groups**: {prof['row_group_count']}\n")
            f.write(f"- **Compression**: `{prof['compression']}`\n\n")
            f.write("| Column Name | Physical Type | Logical Type | Encoded via Dictionary? |\n")
            f.write("|:---|:---|:---|:---:|\n")
            for col in prof["columns"]:
                is_dict = "✅ Yes" if col["dictionary_encoded"] else "❌ Plain"
                logical = f"`{col['logical_type']}`" if col["logical_type"] else "—"
                f.write(f"| `{col['name']}` | `{col['physical_type']}` | {logical} | {is_dict} |\n")
            f.write("\n")

    print(f"📝 Saved provenance markdown register to {md_path}")


if __name__ == "__main__":
    main()
