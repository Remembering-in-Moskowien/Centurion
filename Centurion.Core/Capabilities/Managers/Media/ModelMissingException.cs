namespace Centurion.Core.Capabilities.Managers.Media;

/// <summary>
/// 模型缺失异常：模型本地文件/目录不存在时抛出，消息附带
/// <c>Centurion models install &lt;model&gt;</c> 安装提示。
/// </summary>
public sealed class ModelMissingException(
    string modelName,
    string expectedPath,
    IReadOnlyList<string> missingEntries)
    : InvalidOperationException(BuildMessage(modelName, expectedPath, missingEntries))
{
    /// <summary>缺失的模型名（注册表中的模型名）。</summary>
    public string ModelName { get; } = modelName;

    /// <summary>期望的本地路径（文件或目录）。</summary>
    public string ExpectedPath { get; } = expectedPath;

    /// <summary>缺失的文件条目（单文件模式为模型文件名）。</summary>
    public IReadOnlyList<string> MissingEntries { get; } = missingEntries;

    private static string BuildMessage(string modelName, string expectedPath, IReadOnlyList<string> missingEntries)
    {
        var detail = missingEntries.Count switch
        {
            1 => $"expected at '{expectedPath}'",
            _ => $"expected directory '{expectedPath}' (missing {missingEntries.Count} file(s))"
        };
        return $"Model '{modelName}' is missing ({detail}). " +
               $"Install it first: Centurion models install {modelName}";
    }
}
