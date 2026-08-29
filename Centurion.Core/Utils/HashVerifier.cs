using System.Security.Cryptography;

namespace Centurion.Core.Utils;

public class HashVerifier
{
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

    public sealed class HashVerifyResult
    {
        public bool IsMatch { get; init; }
        public string ActualHash { get; init; } = string.Empty;
    }
}