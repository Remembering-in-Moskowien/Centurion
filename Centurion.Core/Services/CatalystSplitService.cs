using Catalyst;
using Catalyst.Models;
using Mosaik.Core;
using System.Text.RegularExpressions;
using Centurion.Core.Services.Dto;

namespace Centurion.Core.Services
{
    /// <summary>
    /// 基于 Catalyst 的语义分句服务
    /// 保留时间间隙、大写二次分割、短句合并等后处理规则。
    /// 支持多语言，通过语言代码加载对应的 Pipeline。
    /// 新增：对过长句子按 MaxLength / TargetLength 进行二次切分。
    /// </summary>
    public class CatalystSplitService
    {
        // ---------- 常量（与 Python 保持一致） ----------
        private const int TimeGapThresholdMs = 1500;
        private const double MinSentenceDuration = 0.8;
        private const int MaxWordsPerLine = 12;
        private const double MergeGapThreshold = 1.5;
        private const int MinWordsForShort = 3;

        // ---------- Catalyst Pipeline 缓存 ----------
        private readonly Dictionary<Language, Pipeline> _pipelines = new();
        private readonly string _modelCachePath;
        private readonly Language _defaultLanguage;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="modelCachePath">模型缓存目录，Catalyst 会自动下载或读取模型</param>
        /// <param name="defaultLanguage">默认语言，默认为英语</param>
        public CatalystSplitService(string modelCachePath, string defaultLanguage = "en")
        {
            _modelCachePath = modelCachePath;
            _defaultLanguage = GetLanguage(defaultLanguage);

            // 设置存储路径，Catalyst 会将模型下载到此目录
            Storage.Current = new DiskStorage(_modelCachePath);
        }

        /// <summary>
        /// 对外暴露的异步接口（与 SentenceSplitService 的 SplitAsync 签名一致）
        /// </summary>
        public async Task<List<SentenceSplitResult>> SplitAsync(
            SentenceSplitRequest request,
            CancellationToken ct = default)
        {
            return await Task.Run(() => Process(request), ct);
        }

        /// <summary>
        /// 核心处理逻辑
        /// </summary>
        private List<SentenceSplitResult> Process(SentenceSplitRequest request)
        {
            var words = request.Words;
            if (words == null || words.Count == 0)
                return new List<SentenceSplitResult>();

            // 标准化词信息
            var normWords = words.Select(w => new WordInfo
            {
                Text = w.Text.Trim(),
                Start = w.Start,
                End = w.End,
                Speaker = w.Speaker
            }).ToList();

            // 1. 按时间间隙粗分
            var timeSegments = SplitByTimeGap(normWords, TimeGapThresholdMs);

            var allSentences = new List<SentenceResult>();

            // 确定语言
            var language = GetLanguage(request.Language ?? _defaultLanguage.ToString());
            var pipeline = GetPipeline(language);

            foreach (var seg in timeSegments)
            {
                // 拼接完整文本（用于 Catalyst 分句）
                var fullText = string.Join(" ", seg.Select(w => w.Text));
                if (string.IsNullOrWhiteSpace(fullText))
                    continue;

                // 2. Catalyst 句子分割
                string[] sentenceTexts;
                try
                {
                    var doc = new Document(fullText, language);
                    pipeline.ProcessSingle(doc);

                    // 提取句子（使用 span.Value）
                    var sentenceList = new List<string>();
                    if (doc.Spans != null)
                    {
                        foreach (var span in doc.Spans)
                        {
                            sentenceList.Add(span.Value);
                        }
                    }
                    sentenceTexts = sentenceList.Count > 0 ? sentenceList.ToArray() : new[] { fullText };
                }
                catch (Exception ex)
                {
                    // 如果分句失败，退化为整个时间段作为一个句子
                    sentenceTexts = new[] { fullText };
                }

                // 构建字符位置映射（用于将句子文本映射到词列表）
                var wordPositions = BuildWordPositions(seg, fullText);

                // 对每个分句结果，提取对应的词
                foreach (var sentText in sentenceTexts)
                {
                    if (string.IsNullOrWhiteSpace(sentText))
                        continue;

                    var wordsInSent = ExtractWordsForSentence(sentText, wordPositions, fullText);
                    if (wordsInSent.Count == 0)
                        continue;

                    // 3. 按大写字母二次分割（与 Python 一致）
                    var capGroups = SplitByCapitalization(new List<List<WordInfo>> { wordsInSent });
                    foreach (var group in capGroups)
                    {
                        if (group.Count == 0) continue;
                        var subText = string.Join(" ", group.Select(w => w.Text));
                        subText = NormalizePunctuation(subText);
                        allSentences.Add(new SentenceResult
                        {
                            Text = subText,
                            Start = group.First().Start,
                            End = group.Last().End,
                            Words = group
                        });
                    }
                }
            }

            // 按时间排序
            allSentences = allSentences.OrderBy(s => s.Start).ToList();

            // 4. 合并过短句子（与 Python 一致）
            allSentences = MergeShortSentences(allSentences);

            // 5. 按最大长度切分过长句子（新增）
            int maxLen = request.MaxLength ?? 80;
            int targetLen = request.TargetLength ?? 50;
            allSentences = SplitLongSentences(allSentences, maxLen, targetLen);

            // 转换为 DTO
            return allSentences.Select(s => new SentenceSplitResult
            {
                Text = s.Text,
                Start = s.Start,
                End = s.End,
                Words = s.Words.Select(w => new WordTiming
                {
                    Text = w.Text,
                    Start = w.Start,
                    End = w.End,
                    Speaker = w.Speaker
                }).ToList()
            }).ToList();
        }

        // ---------- 辅助方法（规则部分，移植自 Python） ----------

        private List<List<WordInfo>> SplitByTimeGap(List<WordInfo> words, int gapMs)
        {
            if (words == null || words.Count == 0)
                return new List<List<WordInfo>>();

            words = words.OrderBy(w => w.Start).ToList();
            var segments = new List<List<WordInfo>>();
            var current = new List<WordInfo> { words[0] };

            for (int i = 1; i < words.Count; i++)
            {
                var gap = words[i].Start - words[i - 1].End;
                if (gap > gapMs)
                {
                    segments.Add(current);
                    current = new List<WordInfo> { words[i] };
                }
                else
                {
                    current.Add(words[i]);
                }
            }
            if (current.Any())
                segments.Add(current);

            return segments;
        }

        private List<List<WordInfo>> SplitByCapitalization(List<List<WordInfo>> groups)
        {
            var result = new List<List<WordInfo>>();
            foreach (var group in groups)
            {
                if (group.Count < 2)
                {
                    result.Add(group);
                    continue;
                }

                var subGroups = new List<List<WordInfo>>();
                var current = new List<WordInfo> { group[0] };
                for (int i = 1; i < group.Count; i++)
                {
                    var prev = group[i - 1];
                    var curr = group[i];
                    var prevText = prev.Text.Trim();
                    var currText = curr.Text.Trim();

                    if (currText.Length > 0 && char.IsUpper(currText[0]) && currText.Length > 1 &&
                        !(prevText.EndsWith(".") || prevText.EndsWith("!") || prevText.EndsWith("?")) &&
                        !IsAbbreviation(prevText))
                    {
                        subGroups.Add(current);
                        current = new List<WordInfo> { curr };
                    }
                    else
                    {
                        current.Add(curr);
                    }
                }
                if (current.Any())
                    subGroups.Add(current);

                result.AddRange(subGroups);
            }
            return result;
        }

        private bool IsAbbreviation(string text)
        {
            string[] abbrs = { "Mr.", "Mrs.", "Ms.", "Dr.", "e.g.", "i.e.", "vs.", "etc." };
            return abbrs.Contains(text);
        }

        private string NormalizePunctuation(string text)
        {
            return Regex.Replace(text, @"\s+([.,!?;:])", "$1");
        }

        private List<SentenceResult> MergeShortSentences(List<SentenceResult> sentences)
        {
            if (sentences == null || sentences.Count < 2)
                return sentences ?? new List<SentenceResult>();

            var merged = new List<SentenceResult>();
            int i = 0;
            while (i < sentences.Count)
            {
                var cur = sentences[i];
                if (i + 1 >= sentences.Count)
                {
                    merged.Add(cur);
                    break;
                }

                var nxt = sentences[i + 1];
                double curDuration = cur.End - cur.Start;
                int curWordCount = cur.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                double gap = nxt.Start - cur.End;

                bool isShort = (curDuration < MinSentenceDuration && curWordCount <= MinWordsForShort) || curWordCount == 1;

                if (isShort && gap < MergeGapThreshold)
                {
                    int totalWords = curWordCount + nxt.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    if (totalWords <= MaxWordsPerLine)
                    {
                        var mergedText = cur.Text + " " + nxt.Text;
                        var mergedWords = cur.Words.Concat(nxt.Words).ToList();
                        merged.Add(new SentenceResult
                        {
                            Text = mergedText,
                            Start = cur.Start,
                            End = nxt.End,
                            Words = mergedWords
                        });
                        i += 2;
                        continue;
                    }
                }

                merged.Add(cur);
                i++;
            }

            return merged;
        }

        /// <summary>
        /// 将超过指定字符数的句子按词数比例切分为多个句子
        /// </summary>
        private List<SentenceResult> SplitLongSentences(List<SentenceResult> sentences, int maxLength, int targetLength)
        {
            var result = new List<SentenceResult>();
            foreach (var sent in sentences)
            {
                // 如果句子本身不超过最大长度，直接保留
                if (sent.Text.Length <= maxLength)
                {
                    result.Add(sent);
                    continue;
                }

                var words = sent.Words;
                if (words == null || words.Count == 0)
                {
                    result.Add(sent);
                    continue;
                }

                // 计算期望的每个子句的目标词数（按字符比例估算）
                int totalChars = sent.Text.Length;
                // 防止除零
                if (totalChars == 0)
                {
                    result.Add(sent);
                    continue;
                }
                int targetWordsPerChunk = Math.Max(1, (int)((double)targetLength / totalChars * words.Count));
                // 最少保留 2 个词，避免过短
                if (targetWordsPerChunk < 2) targetWordsPerChunk = 2;

                var chunks = new List<List<WordInfo>>();
                var currentChunk = new List<WordInfo>();
                int currentCharCount = 0;

                foreach (var w in words)
                {
                    int wordLen = w.Text.Length + 1; // +1 用于空格
                    // 如果当前块为空，直接加入（即使单词长度超过目标，也要单独成块）
                    if (currentChunk.Count == 0)
                    {
                        currentChunk.Add(w);
                        currentCharCount = wordLen;
                        continue;
                    }

                    // 判断加入当前词是否会超过目标长度或超过目标词数
                    if (currentCharCount + wordLen <= targetLength && currentChunk.Count < targetWordsPerChunk)
                    {
                        currentChunk.Add(w);
                        currentCharCount += wordLen;
                    }
                    else
                    {
                        // 当前块已满，保存并开始新块
                        chunks.Add(currentChunk);
                        currentChunk = new List<WordInfo> { w };
                        currentCharCount = wordLen;
                    }
                }
                if (currentChunk.Any())
                    chunks.Add(currentChunk);

                // 如果没有切分（可能所有词都太大），则回退到原句
                if (chunks.Count <= 1)
                {
                    result.Add(sent);
                    continue;
                }

                // 为每个 chunk 构建新的句子结果，时间按词数比例分配（直接使用首尾词时间）
                foreach (var chunk in chunks)
                {
                    var first = chunk.First();
                    var last = chunk.Last();
                    var text = string.Join(" ", chunk.Select(w => w.Text));
                    text = NormalizePunctuation(text);
                    result.Add(new SentenceResult
                    {
                        Text = text,
                        Start = first.Start,
                        End = last.End,
                        Words = chunk
                    });
                }
            }
            return result;
        }

        // ---------- Catalyst Pipeline 管理 ----------

        private Pipeline GetPipeline(Language language)
        {
            if (!_pipelines.TryGetValue(language, out var pipeline))
            {
                // 注册语言模型（需提前安装对应的 NuGet 包）
                RegisterLanguage(language);

                // 创建仅包含 Tokenizer 和 Sentence Detector 的 Pipeline
                pipeline = Pipeline.TokenizerForAsync(language, sentenceDetector: true).GetAwaiter().GetResult();
                _pipelines[language] = pipeline;
            }
            return pipeline;
        }

        private void RegisterLanguage(Language language)
        {
            // 仅注册已安装的语言包，未安装的会抛出异常
            switch (language)
            {
                case Language.English:
                    Catalyst.Models.English.Register();
                    break;
                // 其他语言按需添加
                // case Language.Chinese:
                //     Catalyst.Models.Chinese.Register();
                //     break;
                // case Language.Spanish:
                //     Catalyst.Models.Spanish.Register();
                //     break;
                // case Language.French:
                //     Catalyst.Models.French.Register();
                //     break;
                // case Language.German:
                //     Catalyst.Models.German.Register();
                //     break;
                default:
                    // 如果语言未明确支持，尝试使用英语作为后备
                    Catalyst.Models.English.Register();
                    break;
            }
        }

        private Language GetLanguage(string languageCode)
        {
            return languageCode?.ToLower() switch
            {
                "en" => Language.English,
                "zh" => Language.Chinese,
                "es" => Language.Spanish,
                "fr" => Language.French,
                "de" => Language.German,
                _ => _defaultLanguage
            };
        }

        // ---------- 辅助：字符位置映射与句子到词的提取 ----------

        private List<(int StartChar, int EndChar, WordInfo Word)> BuildWordPositions(List<WordInfo> words, string fullText)
        {
            var positions = new List<(int, int, WordInfo)>();
            int pos = 0;
            foreach (var w in words)
            {
                int start = pos;
                int end = start + w.Text.Length;
                positions.Add((start, end, w));
                pos = end + 1; // 加一个空格
            }
            return positions;
        }

        private List<WordInfo> ExtractWordsForSentence(string sentence, List<(int Start, int End, WordInfo Word)> wordPositions, string fullText)
        {
            // 在 fullText 中查找句子位置（允许前后空格）
            int idx = fullText.IndexOf(sentence, StringComparison.Ordinal);
            if (idx == -1)
            {
                // 尝试去除首尾空格再查找
                var trimmed = sentence.Trim();
                idx = fullText.IndexOf(trimmed, StringComparison.Ordinal);
                if (idx != -1)
                {
                    int endIdx = idx + trimmed.Length;
                    sentence = fullText.Substring(idx, endIdx - idx);
                }
                else
                {
                    // 如果还找不到，按近似匹配：取所有词（极端情况）
                    return wordPositions.Select(p => p.Word).ToList();
                }
            }

            int sentStart = idx;
            int sentEnd = idx + sentence.Length;

            var result = new List<WordInfo>();
            foreach (var (s, e, w) in wordPositions)
            {
                if (s < sentEnd && e > sentStart)
                    result.Add(w);
            }
            return result;
        }

        // ---------- 内部类 ----------

        private class WordInfo
        {
            public string Text { get; set; }
            public double Start { get; set; }
            public double End { get; set; }
            public string Speaker { get; set; }
        }

        private class SentenceResult
        {
            public string Text { get; set; }
            public double Start { get; set; }
            public double End { get; set; }
            public List<WordInfo> Words { get; set; }
        }
    }
}