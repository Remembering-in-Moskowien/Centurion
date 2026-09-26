using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace Centurion.Core.Capabilities.Localization;

/// <summary>
/// 基于 JSON 资源文件的本地化提供器：从程序根目录 <c>Localization/{culture}.json</c> 读取
/// 扁平键值字典（键即英文默认文本，值即翻译），语言缺失时回退英文（key 本身）。
/// 文件为可编辑的外部资源——新增语言只需放入对应 <c>xx-XX.json</c>，无需重新编译。
/// </summary>
public sealed class JsonStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, JsonStringLocalizer> _cache = new();

    /// <summary>创建 JSON 本地化提供器。</summary>
    /// <param name="directory">JSON 资源目录；为 null 时使用程序根目录下的 Localization。</param>
    public JsonStringLocalizerFactory(string? directory = null)
    {
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "Localization");
    }

    /// <summary>按类型创建本地化器（类型信息被忽略，统一按当前 UI 文化读取资源）。</summary>
    public IStringLocalizer Create(Type resourceSource) => CreateForCulture(CultureInfo.CurrentUICulture.Name);

    /// <summary>按基名创建本地化器（基名被忽略，统一按当前 UI 文化读取资源）。</summary>
    public IStringLocalizer Create(string baseName) => CreateForCulture(CultureInfo.CurrentUICulture.Name);

    /// <summary>按基名与位置创建本地化器（两者均被忽略，统一按当前 UI 文化读取资源）。</summary>
    public IStringLocalizer Create(string baseName, string location) => CreateForCulture(CultureInfo.CurrentUICulture.Name);

    private JsonStringLocalizer CreateForCulture(string cultureName) =>
        _cache.GetOrAdd(cultureName, name =>
        {
            var current = Load(name);
            var fallback = Load("en");
            return new JsonStringLocalizer(current, fallback);
        });

    private IReadOnlyDictionary<string, string> Load(string cultureName)
    {
        var path = Path.Combine(_directory, $"{cultureName}.json");
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // 资源文件损坏时回退到英文（key），不中断程序
            return new Dictionary<string, string>();
        }
    }
}
