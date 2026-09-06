# /// script
# dependencies = [
#     "pyarrow==25.0.0",
# ]
# ///

"""Validate a Parquet file written by the generated C# serializer with PyArrow."""

import os
from datetime import datetime, timezone
from decimal import Decimal

import pyarrow.parquet as pq
import pyarrow as pa


def main() -> None:
    path = os.environ.get("PARQUET_PYARROW_INTEROP_OUTPUT")
    if not path:
        raise SystemExit("PARQUET_PYARROW_INTEROP_OUTPUT must be set")

    table = pq.read_table(path)
    expected_columns = [
        "id",
        "required_name",
        "optional_name",
        "payload",
        "amount",
        "timestamp",
        "status",
    ]
    if table.column_names != expected_columns:
        raise AssertionError(f"schema columns: expected {expected_columns}, actual {table.column_names}")

    rows = table.to_pylist()
    expected = [
        {
            "id": 1,
            "required_name": "one",
            "optional_name": "",
            "payload": b"",
            "amount": Decimal("123.4567"),
            "timestamp": datetime(2024, 6, 15, 12, 30, 0, 123000, tzinfo=timezone.utc),
            "status": 1,
        },
        {
            "id": 2,
            "required_name": "two",
            "optional_name": None,
            "payload": None,
            "amount": Decimal("-0.0001"),
            "timestamp": datetime(2024, 6, 16, 12, 30, 0, 456000, tzinfo=timezone.utc),
            "status": None,
        },
        {
            "id": 3,
            "required_name": "three",
            "optional_name": "three",
            "payload": b"\x00\x01\xff",
            "amount": Decimal("0.0000"),
            "timestamp": datetime(2024, 6, 17, 12, 30, 0, 789000, tzinfo=timezone.utc),
            "status": 2,
        },
    ]
    if len(rows) != len(expected):
        raise AssertionError(f"row count: expected {len(expected)}, actual {len(rows)}")

    for index, (actual, wanted) in enumerate(zip(rows, expected)):
        for column, expected_value in wanted.items():
            if actual[column] != expected_value:
                raise AssertionError(
                    f"row {index}.{column}: expected {expected_value!r}, actual {actual[column]!r}"
                )

    print(f"PyArrow {pa.__version__} validated {len(rows)} generated C# rows from {path}")


if __name__ == "__main__":
    main()
