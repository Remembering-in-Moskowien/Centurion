namespace Centurion.Models;

/// <summary>描述脚本词与音频识别词之间的对齐匹配结果。</summary>
public enum MappingStatus
{
    /// <summary>脚本词与音频词成功对齐。</summary>
    Matched,
    /// <summary>仅存在于脚本、音频中未识别到的词，渲染时通常以占位符补出空隙。</summary>
    ScriptMissing,
    /// <summary>仅存在于音频识别结果、脚本中没有对应文字的词，通常为即兴口语。</summary>
    AudioExtra
}
