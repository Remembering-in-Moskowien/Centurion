using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using Centurion.Core.Exceptions;
using Centurion.Core.Tools;
using Centurion.Core.Operators.Payload;
using Centurion.Core.Operators.Results;

namespace Centurion.Core.Operators.Base;

/// <summary>AI模型算子通用基类，统一封装模型管理、二进制查找、资源释放逻辑</summary>
public abstract class ModelOperatorBase : IOperators
{
    #region 通用字段
    protected readonly string _inputModelName;
    protected readonly ModelMeta _targetMeta;
    protected readonly string _modelFolder;
    protected readonly string _modelFilePath;
    protected Process? _runningProcess;
    protected bool _disposed;
    #endregion

    #region 派生类必须实现的模型字典
    /// <summary>派生类重写：返回当前算子支持的模型字典</summary>
    protected abstract Dictionary<string, ModelMeta> ModelDict { get; }
    #endregion

    #region 构造器（通用模型校验、路径初始化）
    protected ModelOperatorBase(string modelName)
    {
        _inputModelName = modelName.Trim().ToLowerInvariant();
        if (!ModelDict.TryGetValue(_inputModelName, out var tempMeta))
        {
            var supportModels = string.Join("/", ModelDict.Keys);
            throw new ArgumentException(
                Localization.Get("UnsupportedModel", supportModels), 
                nameof(modelName));
        }

        _targetMeta = tempMeta;
        // 统一模型根目录规则：程序运行目录/models/算子分类（派生类可重写）
        _modelFolder = Path.Combine(AppContext.BaseDirectory, "models", GetModelCategoryFolder());
        _modelFilePath = Path.Combine(_modelFolder, _targetMeta.FileName);
    }
    #endregion

    #region 可重写扩展点
    /// <summary>模型分类文件夹名，如 whisper/llama，派生类重写区分模型目录</summary>
    protected virtual string GetModelCategoryFolder() => "common";

    /// <summary>派生类实现：校验二进制、下载模型、哈希校验完整流程</summary>
    public abstract Task EnsureTargetAvailableAsync();
    
    #endregion

    #region 通用工具方法（所有模型算子共用）
    /// <summary>通用二进制定位工具</summary>
    protected virtual string LocateBinary(string binName, params string[] searchFolders)
    {
        return BinaryLocator.Locate(binName, searchFolders);
    }

    /// <summary>通用模型文件哈希校验封装</summary>
    protected virtual void VerifyModelHash()
    {
        var hashResult = SubTools.VerifyHash(_modelFilePath, _targetMeta.Sha256Hash);
        if (!hashResult.IsMatch)
        {
            throw new FileHashMismatchException(
                Localization.Get("FileHashNotMatch"), 
                _modelFilePath,
                _targetMeta.Sha256Hash, 
                hashResult.ActualHash);
        }
    }

    /// <summary>通用模型下载模板（派生类按需调用）</summary>
    protected virtual async Task DownloadModelAsync()
    {
        Directory.CreateDirectory(_modelFolder);
        ConsoleServices.Output.WriteLine(Localization.Get("ModelNotFound", _inputModelName));
        if (!await ConsoleServices.Confirm.ConfirmAsync(Localization.Get("ConfirmContinueInstallation")))
        {
            throw new InvalidOperationException(Localization.Get("CannotContinueWithoutModel"));
        }

        using var aria = new AriaOperator();
        var dlRequest = new OperatorsRequest<AriaDownloadPayload>
        {
            Payload = new AriaDownloadPayload
            {
                Url = _targetMeta.DownloadUrl,
                FullSavePath = _modelFilePath,
                FileHash = _targetMeta.Sha256Hash
            }
        };
        await aria.SendRequestAsync<object, AriaDownloadPayload>(dlRequest);
    }
    #endregion

    #region 统一算子请求抽象接口
    public abstract Task<TResult> SendRequestAsync<TResult, TPayload>(
        OperatorsRequest<TPayload> request, 
        CancellationToken cancellationToken = default);
    #endregion

    #region IDisposable 标准资源释放（进程统一销毁逻辑）
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~ModelOperatorBase()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // 销毁运行中的子进程
            if (_runningProcess != null && !_runningProcess.HasExited)
            {
                _runningProcess.Kill();
                _runningProcess.WaitForExit();
            }
            _runningProcess?.Dispose();
            _runningProcess = null;
        }

        _disposed = true;
    }
    #endregion
}
