namespace Centurion.Core.Tools;

public interface IPythonInterop
{
    /// <summary>
    /// 查找系统中的 Python 解释器（版本 >= 3.9）
    /// </summary>
    Task<string> LocatePythonAsync(CancellationToken ct = default);

    /// <summary>
    /// 确保虚拟环境存在并已安装所有依赖，返回虚拟环境中的 Python 解释器路径。
    /// </summary>
    Task<string> EnsureVirtualEnvironmentAsync(CancellationToken ct = default, params string[] packages);

    /// <summary>
    /// 执行 Python 脚本，通过 stdin 传入 JSON，返回 stdout 的 JSON
    /// </summary>
    Task<string> RunScriptAsync(string pythonPath, string scriptPath, string jsonInput, CancellationToken ct = default);

    /// <summary>
    /// 确保指定包已安装（在虚拟环境中）
    /// </summary>
    Task EnsureDependenciesAsync(string pythonPath, CancellationToken ct = default, params string[] packages);

    Task<string> LocateCondaAsync(CancellationToken ct = default);
    string GetCondaEnvironmentPath();
}