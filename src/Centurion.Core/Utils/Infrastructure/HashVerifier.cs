using System.Security.Cryptography;

namespace Centurion.Core.Utils.Infrastructure;

/// <summary>
/// 文件哈希校验工具，使用 SHA256 计算实际哈希并与期望值比较。
/// </summary>
public class HashVerifier
{
    /// <summary>
    /// 计算指定文件的 SHA256 哈希并与期望哈希值比较。
    /// </summary>
    /// <param name="path">待校验文件的路径。</param>
    /// <param name="hash">期望的小写十六进制 SHA256 哈希值。</param>
    /// <returns>包含是否匹配与实际哈希值的结果对象。</returns>
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
    /// 哈希校验结果。
    /// </summary>
    public sealed class HashVerifyResult
    {
        /// <summary>实际哈希是否与期望哈希一致。</summary>
        public bool IsMatch { get; init; }
        /// <summary>计算得到的实际 SHA256 哈希（小写十六进制）。</summary>
        public string ActualHash { get; init; } = string.Empty;
    }
}