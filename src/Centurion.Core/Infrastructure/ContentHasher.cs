using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Centurion.Core.Infrastructure;

/// <summary>
/// Content-addressed hashing helpers for model files: every model file is stored under its own
/// SHA-256 (lowercase hex), and directory models are stored under an aggregate SHA-256 computed
/// over each member file's relative path and hash, so the directory name uniquely identifies the
/// exact file set regardless of renames.
/// </summary>
public static class ContentHasher
{
    /// <summary>Computes the lowercase-hex SHA-256 of a file's content.</summary>
    public static string ComputeFileSha256(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            throw new FileNotFoundException($"Cannot hash missing file: {path}", path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// Computes the aggregate SHA-256 of a directory model: member entries (relative path + content
    /// hash) are sorted by relative path, serialized as "relpath:sha256" lines and hashed as a whole.
    /// </summary>
    /// <param name="files">Member files as (relative path, absolute path) pairs; relative paths use '/'.</param>
    public static string ComputeAggregateSha256(IReadOnlyList<(string RelativePath, string FilePath)> files)
    {
        if (files is null || files.Count == 0)
            throw new ArgumentException("At least one file is required for an aggregate hash.", nameof(files));

        var entries = files
            .Select(f => (f.RelativePath.Replace('\\', '/'), ComputeFileSha256(f.FilePath)))
            .OrderBy(f => f.Item1, StringComparer.Ordinal)
            .Select(f => $"{f.Item1}:{f.Item2}\n");

        var canonical = string.Concat(entries);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
