#!/usr/bin/env python3
import csv
import glob
import os
import re
import sys

START_MARKER = "<!-- BENCHMARK_TABLE_START -->"
END_MARKER = "<!-- BENCHMARK_TABLE_END -->"

def build_headline_table(csv_files):
    rows = []
    for filepath in csv_files:
        with open(filepath, "r", encoding="utf-8-sig") as f:
            reader = csv.DictReader(f)
            for r in reader:
                method = r.get("Method", "")
                mean = r.get("Mean", "")
                count = r.get("Count", "")
                ratio = r.get("Ratio", "")
                allocated = r.get("Allocated", "")
                alloc_ratio = r.get("Alloc Ratio", "")

                if mean == "NA" or not mean or ratio == "?" or not ratio:
                    continue

                rows.append({
                    "Method": method,
                    "Count": count,
                    "Mean": mean,
                    "Ratio": ratio,
                    "Allocated": allocated,
                    "AllocRatio": alloc_ratio
                })

    if not rows:
        return ""

    md = []
    md.append("## ⚡ Performance & Benchmarks")
    md.append("")
    md.append("Automated BenchmarkDotNet performance baseline comparing **`Parquet.SourceGenerator`** against **`ParquetSerializer` v6** reflection baseline:")
    md.append("")
    md.append("| Benchmark / Method | Row Count | Execution Time | Speed Ratio | Allocated Memory | Memory Ratio |")
    md.append("|:--- |:---:|:---:|:---:|:---:|:---:|")
    for r in rows:
        ratio_val = r["Ratio"]
        alloc_val = r["AllocRatio"]
        ratio_str = f"**{ratio_val}x**" if ratio_val != "1.00" else "1.00x (Baseline)"
        alloc_str = f"**{alloc_val}x**" if alloc_val != "1.00" else "1.00x (Baseline)"
        md.append(f"| `{r['Method']}` | {r['Count']} | `{r['Mean']}` | {ratio_str} | `{r['Allocated']}` | {alloc_str} |")
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
        print(f"Updated benchmark table in {filepath}")
    else:
        print(f"Markers {START_MARKER} not found in {filepath}")

def main():
    results_dir = sys.argv[1] if len(sys.argv) > 1 else "BenchmarkDotNet.Artifacts/results"
    should_update_readme = "--update-readme" in sys.argv
    output_path = sys.argv[2] if len(sys.argv) > 2 and sys.argv[2] != "--update-readme" else None

    csv_files = sorted(glob.glob(os.path.join(results_dir, "*-report.csv")))
    if not csv_files:
        print(f"No benchmark result CSV files found in {results_dir}")
        return

    headline_table = build_headline_table(csv_files)

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
