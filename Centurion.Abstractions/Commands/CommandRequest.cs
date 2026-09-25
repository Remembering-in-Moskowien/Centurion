namespace Centurion.Abstractions.Commands;

/// <summary>
/// 命令请求模型：命令名 + 规范化参数字典。
/// CLI 参数与 JSON 文件都归一化为该模型；未来 JSON-RPC（method=Name, params=Parameters）
/// 与 REST（POST /commands/{Name}）直接复用此契约，执行内核与参数校验零改动。
/// </summary>
/// <param name="Name">命令名，如 "spawn"、"translate"。</param>
/// <param name="Parameters">规范化参数：kebab-case 键 → 值（可为原始 CLR 值或 JSON 元素）。</param>
public sealed record CommandRequest(string Name, Dictionary<string, object?> Parameters);
