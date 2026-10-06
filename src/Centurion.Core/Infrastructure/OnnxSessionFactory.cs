using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;

namespace Centurion.Core.Infrastructure;

/// <summary>
/// Creates ONNX Runtime session options with GPU acceleration preferred. On Windows the DirectML
/// execution provider is tried first (native GPU acceleration, zero CUDA/cuDNN dependency); when no
/// DirectX 12 device is available or the provider fails to initialize, the CPU execution provider is
/// used. With both providers appended, operators unsupported by DirectML automatically fall back to
/// CPU per-op inside a single session.
/// </summary>
public static class OnnxSessionFactory
{
    private static int _dmlNativePreloaded;

    /// <summary>
    /// Builds session options: DirectML (GPU) then CPU when <paramref name="preferGpu"/> is true,
    /// CPU only otherwise.
    /// </summary>
    /// <param name="preferGpu">Whether to attempt the DirectML GPU execution provider first.</param>
    /// <returns>Ready-to-use session options with graph optimization enabled.</returns>
    public static SessionOptions CreateSessionOptions(bool preferGpu = true)
    {
        // Pre-load the DirectML native library before any ONNX Runtime initialization. The managed
        // Microsoft.ML.OnnxRuntime assembly resolves the native "onnxruntime" module from the NuGet
        // cache, where a transitive CPU build (pulled by RapidOcrNet) shadows the DirectML build.
        // Loading the DirectML native first pins the GPU-capable module for the whole process.
        PreloadDmlNative();

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };

        if (preferGpu)
        {
            try
            {
                options.AppendExecutionProvider_DML(0);
                options.AppendExecutionProvider_CPU(0);
            }
            catch
            {
                // DirectML is unavailable (e.g. no DirectX 12 device); CPU only.
                options.AppendExecutionProvider_CPU(0);
            }
        }
        else
        {
            options.AppendExecutionProvider_CPU(0);
        }

        return options;
    }

    /// <summary>
    /// Pins the DirectML native onnxruntime.dll (shipped to the output via Directory.Build.targets)
    /// into the process, so ONNX Runtime always binds the GPU-capable native library.
    /// </summary>
    private static void PreloadDmlNative()
    {
        if (Interlocked.CompareExchange(ref _dmlNativePreloaded, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "onnxruntime.dll");
            if (File.Exists(candidate))
            {
                NativeLibrary.Load(candidate);
            }
        }
        catch
        {
            // Not present or not loadable (e.g. non-Windows layout): fall back to default resolution.
        }
    }
}
