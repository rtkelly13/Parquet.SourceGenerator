using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Parquet.SourceGenerator.Tools.Regression;

/// <summary>
/// A content hash of every file in the checked-in fixture tree at one instant.
/// </summary>
/// <param name="Root">The directory that was hashed.</param>
/// <param name="Files">Relative path to SHA-256, ordinal-ordered.</param>
public sealed record FixtureSnapshot(string Root, IReadOnlyDictionary<string, string> Files)
{
    /// <summary>Gets the number of files covered.</summary>
    public int Count => Files.Count;

    /// <summary>
    /// Gets a single digest over the whole snapshot, suitable for recording in a run manifest.
    /// </summary>
    public string Digest
    {
        get
        {
            string joined = string.Join(
                "\n",
                Files
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + ":" + pair.Value)
            );
            return Convert
                .ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(joined)))
                .ToLowerInvariant();
        }
    }
}

/// <summary>
/// How one fixture path differs between two snapshots.
/// </summary>
/// <param name="Path">Fixture-relative path.</param>
/// <param name="Change">Added, removed or modified.</param>
/// <param name="Before">Hash before the run, if any.</param>
/// <param name="After">Hash after the run, if any.</param>
public sealed record FixtureDifference(string Path, string Change, string? Before, string? After);

/// <summary>
/// Proves that a regression run did not touch the checked-in fixture corpus.
/// </summary>
/// <remarks>
/// The acceptance criterion "no mode mutates checked-in fixtures" cannot be met by convention,
/// because every generator step in the suite writes Parquet files and one mis-set environment
/// variable is enough to write them into <c>test/data</c>. So the tree is hashed before and after
/// every run and any difference fails the run outright, even when every step passed.
/// </remarks>
public static class FixtureGuard
{
    /// <summary>The fixture tree, relative to the repository root.</summary>
    public const string FixtureRoot = "test/data";

    /// <summary>The provenance manifest, relative to the repository root.</summary>
    public const string ManifestPath = "test/data/fixture-manifest.json";

    /// <summary>
    /// Hashes every file under the fixture root.
    /// </summary>
    /// <param name="repositoryRoot">Absolute path to the repository root.</param>
    /// <returns>A snapshot of the tree; empty when the tree is absent.</returns>
    public static FixtureSnapshot Capture(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        string root = Path.Combine(
            repositoryRoot,
            FixtureRoot.Replace('/', Path.DirectorySeparatorChar)
        );
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
        {
            return new FixtureSnapshot(root, files);
        }

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            using FileStream stream = File.OpenRead(file);
            files[relative] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        return new FixtureSnapshot(root, files);
    }

    /// <summary>
    /// Compares two snapshots of the same tree.
    /// </summary>
    /// <param name="before">The snapshot taken before the run.</param>
    /// <param name="after">The snapshot taken after the run.</param>
    /// <returns>Every difference, ordered by path. Empty means the corpus is untouched.</returns>
    public static IReadOnlyList<FixtureDifference> Diff(
        FixtureSnapshot before,
        FixtureSnapshot after
    )
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var differences = new List<FixtureDifference>();
        foreach (KeyValuePair<string, string> entry in before.Files)
        {
            if (!after.Files.TryGetValue(entry.Key, out string? afterHash))
            {
                differences.Add(new FixtureDifference(entry.Key, "removed", entry.Value, null));
            }
            else if (!string.Equals(entry.Value, afterHash, StringComparison.Ordinal))
            {
                differences.Add(
                    new FixtureDifference(entry.Key, "modified", entry.Value, afterHash)
                );
            }
        }

        foreach (KeyValuePair<string, string> entry in after.Files)
        {
            if (!before.Files.ContainsKey(entry.Key))
            {
                differences.Add(new FixtureDifference(entry.Key, "added", null, entry.Value));
            }
        }

        return differences.OrderBy(difference => difference.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Renders a one-line summary of the fixture corpus for the run header.
    /// </summary>
    /// <param name="snapshot">The snapshot to describe.</param>
    /// <returns>A human-readable summary.</returns>
    public static string Describe(FixtureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{snapshot.Count} fixture files, digest {snapshot.Digest}"
        );
    }
}
