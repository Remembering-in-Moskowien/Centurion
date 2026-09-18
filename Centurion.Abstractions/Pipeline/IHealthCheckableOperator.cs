namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// 支持健康检查的管道算子（可选实现）。
/// 用于需要校验外部依赖（如模型文件是否存在、ffmpeg是否安装）的算子。
/// </summary>
public interface IHealthCheckableOperator : IPipelineOperator
{
    /// <summary>
    /// 校验算子运行环境是否就绪。
    /// </summary>
    Task CheckHealthAsync(CancellationToken cancellationToken = default);
}
