using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Centurion.Core.Operators.Base;
using Centurion.Core.Operators.Payload;
using Centurion.Core.Operators.Results;
using Centurion.Core.Tools;
using System.Text;
using System.Text.RegularExpressions;

namespace Centurion.Core.Operators;

/// <summary>
/// 通过外部 Python 进程调用 SAT 模型进行语义分句
/// 继承 ModelOperatorBase，自动管理模型下载和二进制查找
/// </summary>
public class WordMergeOperator(string modelName = "sat-3l-sm") : ModelOperatorBase(modelName)
{
    private string? _pythonPath;
    private string? _scriptPath;
    private bool _environmentReady = false;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    // 模型元数据（与之前一致，使用 ONNX 模型）
    protected override Dictionary<string, ModelMeta> ModelDict => new()
    {
        {
            "sat-3l-sm",
            new ModelMeta(
                "model.onnx",
                "https://hf-mirror.com/segment-any-text/sat-3l-sm/resolve/main/model.onnx",
                "036276f1ed362ceb4191deeeaef20e1931d2b5c1173ea9938a071ff35c062a71"
            )
        }
    };

    protected override string GetModelCategoryFolder() => "text";

    // 确保所有依赖可用：模型、Python 解释器、脚本
    public override async Task EnsureTargetAvailableAsync()
    {
        if (_environmentReady) return;

        await _initLock.WaitAsync();
        try
        {
            if (_environmentReady) return;

            // 1. 查找 Python 解释器
            _pythonPath = await PythonInterop.LocatePythonAsync();
            if (string.IsNullOrEmpty(_pythonPath))
                throw new InvalidOperationException("Python interpreter not found.");

            // 2. 确保 wtpsplit 已安装（自动联网安装）
            await PythonInterop.EnsureDependenciesAsync(_pythonPath);

            // 3. 设置模型缓存目录（HF_HOME），使 Python 模型下载到程序目录
            var modelRoot = Path.Combine(AppContext.BaseDirectory, "models");
            Directory.CreateDirectory(modelRoot);
            Environment.SetEnvironmentVariable("HF_HOME", modelRoot);

            // 4. 写出 Python 脚本（嵌入资源）
            var scriptDir = Path.Combine(AppContext.BaseDirectory, "scripts");
            Directory.CreateDirectory(scriptDir);
            _scriptPath = Path.Combine(scriptDir, "sat_split.py");
            if (!File.Exists(_scriptPath))
            {
                await File.WriteAllTextAsync(_scriptPath, EmbeddedResources.SatSplitScript);
            }

            _environmentReady = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // 核心请求：通过 stdin/stdout 与 Python 进程通信
    public override async Task<TResult> SendRequestAsync<TResult, TPayload>(
        OperatorsRequest<TPayload> request,
        CancellationToken cancellationToken = default)
    {
        // 防御性检查：确保环境已初始化
        await EnsureTargetAvailableAsync();

        if (request.Payload is not WordMergePayload payload)
            throw new ArgumentException("Payload must be WordMergePayload", nameof(request));

        if (string.IsNullOrEmpty(_pythonPath) || string.IsNullOrEmpty(_scriptPath))
            throw new InvalidOperationException("Python environment not properly initialized.");

        // 1. 准备输入 JSON（使用驼峰命名，符合 Python 脚本期望）
        var input = new
        {
            words = payload.Words.Select(w => new { text = w.Text, start = w.Start, end = w.End })
        };
        var jsonInput = JsonSerializer.Serialize(input, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // 2. 调用 Python 脚本
        string jsonOutput;
        try
        {
            jsonOutput = await PythonInterop.RunScriptAsync(
                _pythonPath,
                _scriptPath,
                jsonInput,
                cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Python script execution failed: {ex.Message}", ex);
        }

        var result = JsonSerializer.Deserialize<WordMergeResult>(jsonOutput, _jsonOptions);
        if (result?.Sentences == null)
            throw new InvalidOperationException("Failed to parse Python output.");
        var sentences = result.Sentences;
        
        // 按 Start 排序
        sentences.Sort((a, b) => a.Start.CompareTo(b.Start));

        var cleaned = new List<SentenceResult>();
        SentenceResult? prev = null;

        foreach (var curr in sentences)
        {
            if (prev == null)
            {
                cleaned.Add(curr);
                prev = curr;
                continue;
            }

            // 如果当前句子完全被前一个包含（End <= prev.End），则丢弃
            if (curr.End <= prev.End)
            {
                continue;
            }

            // 如果当前句子开始早于前一个结束，则调整开始为前一个结束
            if (curr.Start < prev.End)
            {
                prev.End = curr.Start;
            }

            // 如果调整后 Start >= End，则丢弃（无效）
            if (curr.Start >= curr.End)
            {
                continue;
            }

            cleaned.Add(curr);
            prev = curr;
        }

        // 用清理后的列表替换原列表
        sentences.Clear();
        sentences.AddRange(cleaned);
            
        foreach (var sent in result.Sentences)
        {
            sent.Text = NormalizeSpaces(sent.Text);
        }
            
        return (TResult)(object)result;
    }
        
    private static readonly Regex MultipleSpacesRegex = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex IllegalSpacesRegex = new(@"\s+(?=[.,!?;:'])", RegexOptions.Compiled);
        
    private static string NormalizeSpaces(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // 1. 将连续多个空白（包括空格、制表符、换行）替换为单个空格
        string normalized = MultipleSpacesRegex.Replace(text, " ");
        // 2. 去除首尾空格
        normalized = normalized.Trim();
        // 3. 处理标点前的多余空格，例如将 "Hello , world" -> "Hello, world"
        normalized = IllegalSpacesRegex.Replace(normalized, "");
        return normalized;
    }
}

// 嵌入资源类（存放 Python 脚本字符串）
internal static class EmbeddedResources
{
    public const string SatSplitScript = """
                                         import sys
                                         import json
                                         import os
                                         import io
                                         from wtpsplit import SaT

                                         # 强制 UTF-8 编码
                                         sys.stdin = io.TextIOWrapper(sys.stdin.buffer, encoding='utf-8')
                                         sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

                                         def main():
                                             try:
                                                 raw = sys.stdin.read()
                                                 if not raw:
                                                     json.dump({'Sentences': []}, sys.stdout)
                                                     return
                                                 data = json.loads(raw)
                                             except json.JSONDecodeError as e:
                                                 print(f"JSON 解析错误: {e}", file=sys.stderr)
                                                 sys.exit(1)

                                             # 兼容大小写：优先使用大写键名
                                             words = data.get('Words') or data.get('words')
                                             if not words:
                                                 json.dump({'Sentences': []}, sys.stdout)
                                                 return

                                             # 统一转换为大写键名，便于后续处理
                                             normalized_words = []
                                             for w in words:
                                                 normalized_words.append({
                                                     'Text': w.get('Text') or w.get('text', ''),
                                                     'Start': w.get('Start') or w.get('start', 0),
                                                     'End': w.get('End') or w.get('end', 0)
                                                 })

                                             normalized_words.sort(key=lambda x: x['Start'])
                                             full_text = ' '.join([w['Text'] for w in normalized_words])

                                             # 模型缓存目录
                                             script_dir = os.path.dirname(os.path.abspath(__file__))
                                             os.environ["HF_HOME"] = os.path.join(script_dir, '..', 'models')

                                             sat = SaT("sat-3l-sm", ort_providers=["CPUExecutionProvider"])
                                             sentences = sat.split(full_text, 
                                             max_length=80, 
                                             prior_type="gaussian",
                                             prior_kwargs={"target_length": 50, "spread": 10})

                                             # 构建词位置映射（三元组：起始字符、结束字符、词字典）
                                             word_positions = []
                                             pos = 0
                                             for w in normalized_words:
                                                 start = pos
                                                 end = start + len(w['Text'])
                                                 word_positions.append((start, end, w))  # 注意：仅三个元素
                                                 pos = end + 1  # 跳过分隔空格

                                             result_sentences = []
                                             search_start = 0
                                             for sent in sentences:
                                                 # 尝试精确匹配
                                                 idx = full_text.find(sent, search_start)
                                                 if idx == -1:
                                                     # 尝试忽略首尾空白
                                                     stripped = sent.strip()
                                                     idx = full_text.find(stripped, search_start)
                                                     if idx != -1:
                                                         sent_end = idx + len(stripped)
                                                         sent = full_text[idx:sent_end]
                                                     else:
                                                         # 完全找不到，跳过该句并推进 search_start 至少1
                                                         print(f"警告: 无法定位句子: '{sent}'", file=sys.stderr)
                                                         search_start += 1
                                                         continue
                                                 else:
                                                     sent_end = idx + len(sent)

                                                 # 找出与 [idx, sent_end) 重叠的词
                                                 overlap_words = []
                                                 for s, e, w in word_positions:  # 解包三元组
                                                     if s < sent_end and e > idx:
                                                         overlap_words.append(w)

                                                 if overlap_words:
                                                     result_sentences.append({
                                                         'Text': sent,
                                                         'Start': min(w['Start'] for w in overlap_words),
                                                         'End': max(w['End'] for w in overlap_words)
                                                     })
                                                 # 无论是否找到，都推进搜索起点
                                                 search_start = sent_end

                                             # 输出符合 C# 模型的大小写要求
                                             json.dump({'Sentences': result_sentences}, sys.stdout)

                                         if __name__ == '__main__':
                                             try:
                                                 main()
                                             except Exception as e:
                                                 import traceback
                                                 traceback.print_exc(file=sys.stderr)
                                                 sys.exit(1)
                                         """;
}