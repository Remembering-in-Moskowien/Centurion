using System.Text.Json;
using System.Text.Json.Serialization;
using Centurion.Core.Operators.Base;
using Centurion.Core.Operators.Payload;
using Centurion.Core.Operators.Results;
using Centurion.Core.Tools;
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
        
        ConsoleServices.Output.WriteLine("分句开始");

        // 1. 准备输入 JSON（使用驼峰命名，符合 Python 脚本期望）
        var input = new
        {
            words = payload.Words.Select(w => new { text = w.Text, start = w.Start, end = w.End }),
            max_length = payload.MaxLength,
            target_length = payload.TargetLength,
            spread_range = payload.SpreadRange
        };
        var jsonInput = JsonSerializer.Serialize(input, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
            ConsoleServices.Output.WriteError($"Python script execution failed: {ex.Message}");
            if (ex.InnerException != null)
                ConsoleServices.Output.WriteError($"Inner exception: {ex.InnerException.Message}");
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
        
        ConsoleServices.Output.WriteLine("分句成功");
            
        return (TResult)(object)result;
    }
        
    private static readonly Regex MultipleSpacesRegex = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex IllegalSpacesRegex = new(@"\s+(?=[.,!?;:'])", RegexOptions.Compiled);
        
    private static string NormalizeSpaces(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // 1. 将连续多个空白（包括空格、制表符、换行）替换为单个空格
        var normalized = MultipleSpacesRegex.Replace(text, " ");
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
    public const string SatSplitScript = """"
                                         import sys
                                         import json
                                         import re
                                         from wtpsplit import SaT
                                         
                                         # 时间间隙阈值（毫秒），可根据需要调整
                                         TIME_GAP_THRESHOLD = 1500  # 适当降低以便更细粒度分段
                                         
                                         def split_by_time_gap(words, gap_threshold_ms):
                                             """
                                             按单词间的时间间隙将单词列表切分成多个段落
                                             """
                                             if not words:
                                                 return []
                                             words = sorted(words, key=lambda x: x['start'])
                                             segments = []
                                             current_seg = [words[0]]
                                             for i in range(1, len(words)):
                                                 gap = words[i]['start'] - words[i-1]['end']
                                                 if gap > gap_threshold_ms:
                                                     segments.append(current_seg)
                                                     current_seg = [words[i]]
                                                 else:
                                                     current_seg.append(words[i])
                                             if current_seg:
                                                 segments.append(current_seg)
                                             return segments
                                         
                                         def split_by_punctuation(words):
                                             """
                                             根据句号、问号、感叹号将一个段落拆分成多个子段落
                                             返回子段落列表，每个子段落是单词列表
                                             """
                                             if not words:
                                                 return []
                                             # 尝试找到每个单词文本是否以 .!? 结尾（可能包含后续空格）
                                             sub_segments = []
                                             current = []
                                             for w in words:
                                                 current.append(w)
                                                 # 检查文本末尾是否为 . ! ?（忽略可能的前后空格）
                                                 if w['text'].strip().endswith(('.', '!', '?')):
                                                     sub_segments.append(current)
                                                     current = []
                                             if current:
                                                 sub_segments.append(current)
                                             # 如果没有标点，整个作为一个段落
                                             return sub_segments if sub_segments else [words]
                                         
                                         def main():
                                             # 读取输入
                                             raw = sys.stdin.read()
                                             if not raw:
                                                 json.dump({'sentences': []}, sys.stdout)
                                                 return
                                             try:
                                                 data = json.loads(raw)
                                             except json.JSONDecodeError as e:
                                                 json.dump({'error': f'JSON decode error: {e}'}, sys.stdout)
                                                 return
                                         
                                             words = data.get('words') or data.get('Words')
                                             if not words:
                                                 json.dump({'sentences': []}, sys.stdout)
                                                 return
                                         
                                             # 统一键名为小写
                                             norm_words = []
                                             for w in words:
                                                 norm_words.append({
                                                     'text': w.get('text') or w.get('Text', ''),
                                                     'start': w.get('start') or w.get('Start', 0),
                                                     'end': w.get('end') or w.get('End', 0)
                                                 })
                                         
                                             # 按时间间隙分段
                                             time_segments = split_by_time_gap(norm_words, TIME_GAP_THRESHOLD)
                                             if not time_segments:
                                                 json.dump({'sentences': []}, sys.stdout)
                                                 return
                                         
                                             # 加载 SAT 模型（只一次）
                                             sat = SaT("sat-3l-sm", ort_providers=["CPUExecutionProvider"])
                                         
                                             all_sentences = []
                                         
                                             for seg in time_segments:
                                                 # 按标点进一步细分
                                                 sub_segments = split_by_punctuation(seg)
                                                 for sub in sub_segments:
                                                     if not sub:
                                                         continue
                                                     full_text = ' '.join([w['text'] for w in sub])
                                                     if not full_text.strip():
                                                         continue
                                         
                                                     # 调用 SAT 分句（可调整 max_length 等参数）
                                                     try:
                                                         sentences = sat.split(full_text,
                                                             max_length=50,
                                                             prior_type="gaussian",
                                                             prior_kwargs={"target_length": 30, "spread": 10})
                                                     except Exception as e:
                                                         # 记录错误但继续处理其他段
                                                         print(f"SAT split error: {e}", file=sys.stderr)
                                                         continue
                                         
                                                     # 构建字符位置映射
                                                     word_positions = []
                                                     pos = 0
                                                     for w in sub:
                                                         start = pos
                                                         end = start + len(w['text'])
                                                         word_positions.append((start, end, w))
                                                         pos = end + 1
                                         
                                                     # 将句子映射到时间
                                                     search_start = 0
                                                     for sent in sentences:
                                                         idx = full_text.find(sent, search_start)
                                                         if idx == -1:
                                                             # 尝试去掉首尾空格
                                                             stripped = sent.strip()
                                                             idx = full_text.find(stripped, search_start)
                                                             if idx != -1:
                                                                 sent_end = idx + len(stripped)
                                                                 sent = full_text[idx:sent_end]
                                                             else:
                                                                 search_start += 1
                                                                 continue
                                                         else:
                                                             sent_end = idx + len(sent)
                                         
                                                         overlap = []
                                                         for s, e, w in word_positions:
                                                             if s < sent_end and e > idx:
                                                                 overlap.append(w)
                                         
                                                         if overlap:
                                                             all_sentences.append({
                                                                 'text': sent,
                                                                 'start': min(w['start'] for w in overlap),
                                                                 'end': max(w['end'] for w in overlap)
                                                             })
                                                         search_start = sent_end
                                         
                                             # 按时间排序
                                             all_sentences.sort(key=lambda x: x['start'])
                                         
                                             # 输出结果
                                             json.dump({'sentences': all_sentences}, sys.stdout)
                                         
                                         if __name__ == '__main__':
                                             try:
                                                 main()
                                             except Exception as e:
                                                 import traceback
                                                 error_payload = {
                                                     'error': str(e),
                                                     'traceback': traceback.format_exc()
                                                 }
                                                 json.dump(error_payload, sys.stdout)
                                                 traceback.print_exc(file=sys.stderr)
                                                 sys.exit(1)
                                         """";
}