namespace Centurion.Abstractions;

/// <summary>
/// 标准化 CLI 退出码（脚本可直接依据退出码判断结果）。
/// </summary>
public static class ExitCodes
{
    /// <summary>成功。</summary>
    public const int Success = 0;

    /// <summary>执行失败（模型缺失、管道错误、IO 错误等）。</summary>
    public const int Failure = 1;

    /// <summary>用法错误（未知命令/参数、配置非法）——Spectre 解析错误仍由 Spectre 自身处理。</summary>
    public const int Usage = 2;

    /// <summary>用户取消（Ctrl+C）。</summary>
    public const int Cancelled = 130;
}
