# /// script
# dependencies = [
#     "pyarrow",
# ]
# ///

"""
Verifies that all Git LFS Parquet files under benchmarks/data/ are:
1. Properly hydrated (not raw LFS text pointers).
2. Cryptographically identical to the recorded provenance.json SHA-256 digest.
3. Structurally valid with expected row count and column count.
"""

import hashlib
import json
import os
import sys
import pyarrow.parquet as pq

BENCHMARK_DATA_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "benchmarks", "data"))
MANIFEST_PATH = os.path.join(BENCHMARK_DATA_DIR, "provenance.json")


def verify_provenance():
    if not os.path.exists(MANIFEST_PATH):
        print(f"❌ Provenance manifest not found: {MANIFEST_PATH}")
        sys.exit(1)

    with open(MANIFEST_PATH, "r", encoding="utf-8") as f:
        manifest = json.load(f)

    datasets = manifest.get("datasets", [])
    if not datasets:
        print(f"❌ No datasets found in manifest {MANIFEST_PATH}")
        sys.exit(1)

    print(f"🔍 Verifying provenance and integrity for {len(datasets)} dataset(s)...")
    errors = []

    for ds in datasets:
        file_name = ds["filename"]
        file_path = os.path.join(BENCHMARK_DATA_DIR, file_name)

        if not os.path.exists(file_path):
            errors.append(f"Missing file: {file_path}")
            continue

        file_size = os.path.getsize(file_path)
        if file_size < 1024:
            errors.append(
                f"{file_name} is only {file_size} bytes! Likely an unhydrated Git LFS pointer. "
                f"Run 'git lfs pull' or check actions/checkout lfs configuration."
            )
            continue

        # Check SHA-256
        hasher = hashlib.sha256()
        with open(file_path, "rb") as f:
            for chunk in iter(lambda: f.read(65536), b""):
                hasher.update(chunk)
        digest = hasher.hexdigest()

        expected_sha = ds["integrity"]["sha256"]
        if digest != expected_sha:
            errors.append(
                f"SHA-256 mismatch for {file_name}: "
                f"expected {expected_sha}, got {digest}"
            )
            continue

        # Check structural validity
        try:
            pf = pq.ParquetFile(file_path)
            actual_rows = pf.metadata.num_rows
            expected_rows = ds["parquet_profile"]["row_count"]
            if actual_rows != expected_rows:
                errors.append(
                    f"Row count mismatch for {file_name}: "
                    f"expected {expected_rows}, got {actual_rows}"
                )
                continue

            actual_cols = pf.metadata.num_columns
            expected_cols = ds["parquet_profile"]["column_count"]
            if actual_cols != expected_cols:
                errors.append(
                    f"Column count mismatch for {file_name}: "
                    f"expected {expected_cols}, got {actual_cols}"
                )
                continue

            print(f"  ✅ {ds['id']} ({file_name}): Verified SHA-256 & {actual_rows:,} rows")

        except Exception as ex:
            errors.append(f"Failed to read Parquet file {file_name}: {ex}")

    if errors:
        print("\n❌ Provenance verification failed with the following error(s):")
        for err in errors:
            print(f"  - {err}")
        sys.exit(1)

    print("\n🎉 All benchmark datasets passed provenance and cryptographic integrity checks!")


if __name__ == "__main__":
    verify_provenance()
