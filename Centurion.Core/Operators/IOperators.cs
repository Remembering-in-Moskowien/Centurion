namespace Centurion.Core.Operators;

/// <summary>
/// 下载算子通用接口
/// </summary>
public interface IOperators : IDisposable
{
    /// <summary>
    /// 校验底层执行程序是否可用
    /// </summary>
    Task EnsureTargetAvailableAsync();

    /// <summary>
    /// 发送算子请求，全异步支持取消
    /// </summary>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    /// <typeparam name="TPayload">请求载荷类型</typeparam>
    /// <param name="request">请求包</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<TResult> SendRequestAsync<TResult, TPayload>(
        OperatorsRequest<TPayload> request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 算子请求载体
/// </summary>
/// <typeparam name="TPayload">业务载荷</typeparam>
public class OperatorsRequest<TPayload>
{
    public required TPayload Payload { get; set; }
}

/// <summary>
/// 下载进度回调委托，解耦Spectre控制台
/// </summary>
/// <param name="downloadedBytes">已下载字节</param>
/// <param name="totalBytes">总字节</param>
/// <param name="speed">速度文本</param>
/// <param name="eta">剩余时间文本</param>
public delegate void DownloadProgressUpdate(long downloadedBytes, long totalBytes, string speed, string eta);