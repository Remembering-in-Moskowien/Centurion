using System.Security.Cryptography;

namespace Centurion.Core.Utils.Infrastructure;

/// <summary>
/// File hash verification utility: computes the actual hash with SHA256 and compares it against the expected value.
/// </summary>
public class HashVerifier
{
    /// <summary>
    /// Computes the SHA256 hash of the given file and compares it against the expected hash.
    /// </summary>
    /// <param name="path">Path of the file to verify.</param>
    /// <param name="hash">Expected lowercase hexadecimal SHA256 hash.</param>
    /// <returns>A result object containing whether it matches and the actual hash.</returns>
    public static HashVerifyResult VerifyHash(string path, string hash)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var actualHash = Convert.ToHexStringLower(sha256.ComputeHash(stream));
        return new HashVerifyResult
        {
            IsMatch = string.Equals(actualHash, hash, StringComparison.OrdinalIgnoreCase),
            ActualHash = actualHash
        };
    }

    /// <summary>
    /// Hash verification result.
    /// </summary>
    public sealed class HashVerifyResult
    {
        /// <summary>Whether the actual hash matches the expected hash.</summary>
        public bool IsMatch { get; init; }
        /// <summary>The computed actual SHA256 hash (lowercase hexadecimal).</summary>
        public string ActualHash { get; init; } = string.Empty;
    }
}