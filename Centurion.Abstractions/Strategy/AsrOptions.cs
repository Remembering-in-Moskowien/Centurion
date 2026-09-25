namespace Centurion.Abstractions.Strategy;

using Centurion.Models.Asr;

/// <summary>
/// 云端 ASR 连接配置：提供商、密钥与端点（本地转录策略不使用此配置）。
/// </summary>
/// <param name="Provider">云端 ASR 提供商。</param>
/// <param name="ApiKey">提供商 API 密钥。</param>
/// <param name="BaseUrl">自定义端点；为空时按提供商默认。</param>
public sealed record AsrOptions(AsrProvider Provider, string? ApiKey, string? BaseUrl);
