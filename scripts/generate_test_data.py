# /// script
# dependencies = [
#     "pyarrow",
#     "numpy",
# ]
# ///

"""
Deterministic Multi-Version Parquet Test Data Generator
Generates test datasets at varying scales and complexity levels using PyArrow.
Outputs test data for multiple major Parquet specification versions (Format v1.0 and v2.6)
to guarantee backwards compatibility across legacy and modern C# Parquet.Net major versions.
"""

import os
import sys
import uuid
from decimal import Decimal
import pyarrow as pa
import pyarrow.parquet as pq

BASE_OUTPUT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "test", "data"))
VERSIONS = {
    "v1": "1.0",
    "v2": "2.6"
}


def ensure_output_dirs():
    for version_dir in VERSIONS.keys():
        path = os.path.join(BASE_OUTPUT_DIR, version_dir)
        os.makedirs(path, exist_ok=True)
        print(f"Version directory ready: {path}")


def generate_01_small_flat_primitives(v_key, v_spec):
    count = 100
    ids = list(range(count))
    names = [f"user_{i}" for i in ids]
    scores = [(i * 1.5) % 100.0 for i in ids]
    is_active = [(i % 2 == 0) for i in ids]
    timestamps = [1700000000000 + (i * 1000) for i in ids]

    schema = pa.schema([
        pa.field("id", pa.int32(), nullable=False),
        pa.field("name", pa.string(), nullable=False),
        pa.field("score", pa.float64(), nullable=False),
        pa.field("is_active", pa.bool_(), nullable=False),
        pa.field("created_at_ms", pa.int64(), nullable=False),
    ])

    table = pa.Table.from_arrays([
        pa.array(ids, type=pa.int32()),
        pa.array(names, type=pa.string()),
        pa.array(scores, type=pa.float64()),
        pa.array(is_active, type=pa.bool_()),
        pa.array(timestamps, type=pa.int64()),
    ], schema=schema)

    filepath = os.path.join(BASE_OUTPUT_DIR, v_key, "01_small_flat_primitives.parquet")
    pq.write_table(table, filepath, version=v_spec, use_dictionary=False, compression="snappy")
    print(f"  [Format {v_spec}] Generated {filepath} ({count} rows)")


def generate_02_medium_nullable_types(v_key, v_spec):
    count = 10_000
    ids = list(range(count))
    
    nullable_ints = [None if i % 5 == 0 else i * 10 for i in ids]
    nullable_doubles = [None if i % 5 == 0 else (i * 3.14159) % 1000.0 for i in ids]
    nullable_strings = [None if i % 5 == 0 else f"str_val_{i}" for i in ids]
    nullable_bools = [None if i % 5 == 0 else (i % 3 == 0) for i in ids]

    schema = pa.schema([
        ("id", pa.int32()),
        ("nullable_int", pa.int32()),
        ("nullable_double", pa.float64()),
        ("nullable_string", pa.string()),
        ("nullable_bool", pa.bool_()),
    ])

    table = pa.Table.from_arrays([
        pa.array(ids, type=pa.int32()),
        pa.array(nullable_ints, type=pa.int32()),
        pa.array(nullable_doubles, type=pa.float64()),
        pa.array(nullable_strings, type=pa.string()),
        pa.array(nullable_bools, type=pa.bool_()),
    ], schema=schema)

    filepath = os.path.join(BASE_OUTPUT_DIR, v_key, "02_medium_nullable_types.parquet")
    pq.write_table(table, filepath, version=v_spec, compression="snappy")
    print(f"  [Format {v_spec}] Generated {filepath} ({count} rows)")


def generate_03_complex_decimals_guids(v_key, v_spec):
    count = 5_000
    ids = list(range(count))
    
    guid_strs = [str(uuid.UUID(int=i + 0x123456789ABCDEF00000000000000000)) for i in ids]
    decimals = [Decimal(f"{(i * 123.4567) % 99999.9999:.4f}") for i in ids]
    timestamps_us = [1700000000000000 + (i * 100000) for i in ids]
    category_enums = [(i % 4) for i in ids]

    schema = pa.schema([
        ("id", pa.int32()),
        ("guid_str", pa.string()),
        ("amount", pa.decimal128(18, 4)),
        ("timestamp_us", pa.timestamp("us")),
        ("category", pa.int32()),
    ])

    table = pa.Table.from_arrays([
        pa.array(ids, type=pa.int32()),
        pa.array(guid_strs, type=pa.string()),
        pa.array(decimals, type=pa.decimal128(18, 4)),
        pa.array(timestamps_us, type=pa.timestamp("us")),
        pa.array(category_enums, type=pa.int32()),
    ], schema=schema)

    filepath = os.path.join(BASE_OUTPUT_DIR, v_key, "03_complex_decimals_guids.parquet")
    pq.write_table(table, filepath, version=v_spec, compression="snappy")
    print(f"  [Format {v_spec}] Generated {filepath} ({count} rows)")


def generate_04_nested_lists_maps(v_key, v_spec):
    count = 1_000
    ids = list(range(count))
    
    tags_list = [["primary", f"tag_{i % 10}", f"sub_{i % 3}"] for i in ids]
    scores_list = [[i, i + 1, i + 2] for i in ids]
    metadata_maps = [
        [("env", "production" if i % 2 == 0 else "staging"), ("index", str(i))]
        for i in ids
    ]

    schema = pa.schema([
        ("id", pa.int32()),
        ("tags", pa.list_(pa.string())),
        ("scores", pa.list_(pa.int32())),
        ("metadata", pa.map_(pa.string(), pa.string())),
    ])

    table = pa.Table.from_arrays([
        pa.array(ids, type=pa.int32()),
        pa.array(tags_list, type=pa.list_(pa.string())),
        pa.array(scores_list, type=pa.list_(pa.int32())),
        pa.array(metadata_maps, type=pa.map_(pa.string(), pa.string())),
    ], schema=schema)

    filepath = os.path.join(BASE_OUTPUT_DIR, v_key, "04_nested_lists_maps.parquet")
    pq.write_table(table, filepath, version=v_spec, compression="snappy")
    print(f"  [Format {v_spec}] Generated {filepath} ({count} rows)")


def generate_05_large_scale_flat(v_key, v_spec):
    count = 100_000
    ids = list(range(count))
    payloads = [f"payload_data_string_buffer_segment_{i % 500}" for i in ids]
    val_a = [i * 7 for i in ids]
    val_b = [(i * 0.123456789) for i in ids]
    is_valid = [(i % 7 != 0) for i in ids]

    schema = pa.schema([
        ("id", pa.int64()),
        ("payload", pa.string()),
        ("val_a", pa.int32()),
        ("val_b", pa.float64()),
        ("is_valid", pa.bool_()),
    ])

    table = pa.Table.from_arrays([
        pa.array(ids, type=pa.int64()),
        pa.array(payloads, type=pa.string()),
        pa.array(val_a, type=pa.int32()),
        pa.array(val_b, type=pa.float64()),
        pa.array(is_valid, type=pa.bool_()),
    ], schema=schema)

    filepath = os.path.join(BASE_OUTPUT_DIR, v_key, "05_large_scale_flat.parquet")
    pq.write_table(table, filepath, version=v_spec, compression="snappy")
    print(f"  [Format {v_spec}] Generated {filepath} ({count} rows)")


def main():
    print("Starting multi-version Parquet test data generation...")
    ensure_output_dirs()
    
    for v_key, v_spec in VERSIONS.items():
        print(f"\nGenerating Datasets for Parquet Specification Version: {v_spec} ({v_key})")
        generate_01_small_flat_primitives(v_key, v_spec)
        generate_02_medium_nullable_types(v_key, v_spec)
        generate_03_complex_decimals_guids(v_key, v_spec)
        generate_04_nested_lists_maps(v_key, v_spec)
        generate_05_large_scale_flat(v_key, v_spec)

    print("\nMulti-version Parquet test data generation complete!")


if __name__ == "__main__":
    main()
