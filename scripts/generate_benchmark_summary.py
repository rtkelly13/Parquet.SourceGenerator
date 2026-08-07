#!/usr/bin/env python3
import csv
import glob
import os
import sys

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
    md.append("## 🏆 Headline Performance Summary")
    md.append("")
    md.append("| Method / Benchmark | Row Count | Execution Time | Speed Ratio | Allocated Memory | Memory Ratio |")
    md.append("|:--- |:---:|:---:|:---:|:---:|:---:|")
    for r in rows:
        ratio_val = r["Ratio"]
        alloc_val = r["AllocRatio"]
        ratio_str = f"**{ratio_val}x**" if ratio_val != "1.00" else "1.00x (Baseline)"
        alloc_str = f"**{alloc_val}x**" if alloc_val != "1.00" else "1.00x (Baseline)"
        md.append(f"| `{r['Method']}` | {r['Count']} | `{r['Mean']}` | {ratio_str} | `{r['Allocated']}` | {alloc_str} |")
    md.append("")
    return "\n".join(md)

def main():
    results_dir = sys.argv[1] if len(sys.argv) > 1 else "BenchmarkDotNet.Artifacts/results"
    output_path = sys.argv[2] if len(sys.argv) > 2 else None

    csv_files = sorted(glob.glob(os.path.join(results_dir, "*-report.csv")))
    if not csv_files:
        content = "No benchmark result CSV files found."
    else:
        headline = build_headline_table(csv_files)

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

        content = headline + "\n" + "\n".join(details)

    if output_path:
        with open(output_path, "w", encoding="utf-8") as f:
            f.write(content)
        print(f"Benchmark summary written to {output_path}")
    else:
        print(content)

if __name__ == "__main__":
    main()
