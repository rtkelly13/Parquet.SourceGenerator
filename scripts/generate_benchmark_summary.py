#!/usr/bin/env python3
import csv
import glob
import os
import re
import sys

START_MARKER = "<!-- BENCHMARK_TABLE_START -->"
END_MARKER = "<!-- BENCHMARK_TABLE_END -->"

def format_time(mean_str):
    if not mean_str or mean_str in ("NA", "?"):
        return "N/A"
    clean = mean_str.replace(",", "").strip()
    if "μs" in clean:
        val = float(clean.replace("μs", "").strip())
        if val >= 1000:
            return f"{val / 1000:.2f} ms"
        return f"{val:.1f} μs"
    elif "ms" in clean:
        val = float(clean.replace("ms", "").strip())
        return f"{val:.2f} ms"
    elif "s" in clean:
        val = float(clean.replace("s", "").strip())
        return f"{val:.2f} s"
    return mean_str

def format_memory(alloc_str):
    if not alloc_str or alloc_str in ("NA", "?", "-"):
        return "N/A"
    clean = alloc_str.replace(",", "").strip()
    if "KB" in clean:
        val = float(clean.replace("KB", "").strip())
        if val >= 1024:
            return f"{val / 1024:.2f} MB"
        return f"{val:.1f} KB"
    elif "MB" in clean:
        val = float(clean.replace("MB", "").strip())
        return f"{val:.2f} MB"
    elif "B" in clean:
        val = float(clean.replace("B", "").strip())
        if val >= 1024 * 1024:
            return f"{val / (1024 * 1024):.2f} MB"
        elif val >= 1024:
            return f"{val / 1024:.1f} KB"
        return f"{int(val)} B"
    return alloc_str

def parse_number(val_str):
    if not val_str or val_str in ("NA", "?"):
        return None
    clean = val_str.replace(",", "").strip()
    match = re.search(r"([0-9.]+)", clean)
    if match:
        return float(match.group(1))
    return None

def build_headline_table(results_dir):
    csv_files = glob.glob(os.path.join(results_dir, "*-report.csv"))
    entries = {}

    for filepath in csv_files:
        with open(filepath, "r", encoding="utf-8-sig") as f:
            reader = csv.DictReader(f)
            for r in reader:
                method = r.get("Method", "")
                count_str = r.get("Count", "0")
                mean_str = r.get("Mean", "")
                alloc_str = r.get("Allocated", "")
                ratio_str = r.get("Ratio", "")
                alloc_ratio_str = r.get("Alloc Ratio", "")

                if not mean_str or mean_str == "NA":
                    continue

                count = int(count_str) if count_str.isdigit() else 0
                key = (method, count)
                entries[key] = {
                    "method": method,
                    "count": count,
                    "mean_raw": mean_str,
                    "mean_fmt": format_time(mean_str),
                    "mean_num": parse_number(mean_str),
                    "alloc_raw": format_memory(alloc_str),
                    "ratio_num": parse_number(ratio_str),
                    "alloc_ratio_num": parse_number(alloc_ratio_str),
                }

    scenarios = [
        {
            "title": "File Serialization (Write)",
            "baseline_method": "ReflectionParquetSerializerV6Write",
            "sg_method": "SourceGeneratorWriteAsync",
            "target_count": 100000,
        },
        {
            "title": "Streaming Batched Write",
            "baseline_method": "ReflectionParquetSerializerV6Write",
            "sg_method": "SourceGeneratorWriteBatchedAsync",
            "target_count": 100000,
        },
        {
            "title": "File Deserialization (Read)",
            "baseline_method": "WriteReflectionParquetConvert",
            "sg_method": "ReadSourceGenerator",
            "target_count": 10000,
        },
        {
            "title": "Guid Serialization",
            "baseline_method": "ReflectionParquetSerializerGuidWrite",
            "sg_method": "SourceGeneratorGuidWriteAsync",
            "target_count": 10000,
        },
    ]

    table_rows = []
    for s in scenarios:
        count = s["target_count"]
        b_entry = entries.get((s["baseline_method"], count))
        sg_entry = entries.get((s["sg_method"], count))

        if not b_entry or not sg_entry:
            alt_counts = [100000, 10000, 1000]
            for c in alt_counts:
                b = entries.get((s["baseline_method"], c))
                sg = entries.get((s["sg_method"], c))
                if b and sg:
                    b_entry, sg_entry, count = b, sg, c
                    break

        if not b_entry or not sg_entry:
            continue

        b_time = b_entry["mean_fmt"]
        sg_time = sg_entry["mean_fmt"]
        b_alloc = b_entry["alloc_raw"]
        sg_alloc = sg_entry["alloc_raw"]

        speedup_str = "—"
        ratio = sg_entry["ratio_num"]
        if ratio and ratio > 0:
            if ratio < 1.0:
                speedup_val = 1.0 / ratio
                speedup_str = f"⚡ **{speedup_val:.1f}x faster**"
            else:
                speedup_str = f"{ratio:.2f}x baseline"
        elif b_entry["mean_num"] and sg_entry["mean_num"] and sg_entry["mean_num"] > 0:
            speedup_val = b_entry["mean_num"] / sg_entry["mean_num"]
            speedup_str = f"⚡ **{speedup_val:.1f}x faster**"

        mem_str = "—"
        alloc_ratio = sg_entry["alloc_ratio_num"]
        if alloc_ratio and alloc_ratio > 0:
            if alloc_ratio < 1.0:
                saved_pct = int(round((1.0 - alloc_ratio) * 100))
                mem_str = f"📉 **{saved_pct}% less memory**"
            else:
                mem_str = f"{alloc_ratio:.2f}x alloc"

        count_formatted = f"{count:,}"
        table_rows.append(
            f"| **{s['title']}** | {count_formatted} items | {b_time} ({b_alloc}) | **{sg_time}** (**{sg_alloc}**) | {speedup_str} | {mem_str} |"
        )

    if not table_rows:
        return ""

    md = []
    md.append("## ⚡ Performance & Benchmarks")
    md.append("")
    md.append("Zero-reflection C# source generation vs **`ParquetSerializer` v6** reflection baseline:")
    md.append("")
    md.append("| Operation | Scale | Reflection Baseline | Source Generator | Speedup | Memory Reduction |")
    md.append("|:--- |:---:|:---:|:---:|:---:|:---:|")
    md.extend(table_rows)
    md.append("")
    md.append("> 📌 **Note**: BenchmarkDotNet results captured on GitHub Actions. Detailed multi-scale reports (1K, 10K, 100K, 1M rows) are in [docs/BENCHMARKS.md](https://github.com/rtkelly13/Parquet.SourceGenerator/blob/main/docs/BENCHMARKS.md).")
    md.append("")
    return "\n".join(md)

def update_readme_file(filepath, table_md):
    if not os.path.exists(filepath):
        return

    with open(filepath, "r", encoding="utf-8") as f:
        content = f.read()

    pattern = re.compile(f"{re.escape(START_MARKER)}.*?{re.escape(END_MARKER)}", re.DOTALL)
    replacement = f"{START_MARKER}\n{table_md}\n{END_MARKER}"

    if pattern.search(content):
        new_content = pattern.sub(replacement, content)
        with open(filepath, "w", encoding="utf-8") as f:
            f.write(new_content)
        print(f"Updated headline benchmark table in {filepath}")
    else:
        print(f"Markers {START_MARKER} not found in {filepath}")

def main():
    results_dir = sys.argv[1] if len(sys.argv) > 1 else "BenchmarkDotNet.Artifacts/results"
    should_update_readme = "--update-readme" in sys.argv
    output_path = sys.argv[2] if len(sys.argv) > 2 and sys.argv[2] != "--update-readme" else None

    headline_table = build_headline_table(results_dir)

    md_files = sorted(glob.glob(os.path.join(results_dir, "*-report-github.md")))
    details = []
    details.append("## 📊 Detailed BenchmarkDotNet Reports")
    details.append("")
    for md_file in md_files:
        suite = os.path.basename(md_file).replace("-report-github.md", "").split(".")[-1]
        details.append(f"### {suite}")
        details.append("")
        with open(md_file, "r", encoding="utf-8") as f:
            details.append(f.read().strip())
        details.append("")

    full_report = headline_table + "\n" + "\n".join(details)

    if should_update_readme:
        update_readme_file("README.md", headline_table)
        update_readme_file("PACKAGE_README.md", headline_table)

    if output_path:
        with open(output_path, "w", encoding="utf-8") as f:
            f.write(full_report)
        print(f"Benchmark summary written to {output_path}")
    elif not should_update_readme:
        print(full_report)

if __name__ == "__main__":
    main()
