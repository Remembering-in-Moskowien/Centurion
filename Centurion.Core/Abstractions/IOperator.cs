namespace Centurion.Core.Abstractions;

/// <summary>
/// 下载算子通用接口
/// </summary>
public interface IOperator<TRequest, TResponse> : IDisposable
{
    /// <summary>
    /// 校验底层执行程序是否可用
    /// </summary>
    Task EnsureTargetAvailableAsync();

    /// <summary>
    /// 发送算子请求，全异步支持取消
    /// </summary>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    /// <typeparam name="TRequest">请求载荷类型</typeparam>
    /// <param name="request">请求包</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<TResponse> ProcessAsync(
        OperatorsRequest<TRequest> request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 算子请求载体
/// </summary>
/// <typeparam name="TPayload">业务载荷</typeparam>
public class OperatorsRequest<TPayload>
{
    public required TPayload Payload { get; init; }
}