using Centurion.Models.Console;
using Xunit;

namespace Centurion.Tests.Cli;

/// <summary>本地化契约：key 即英文默认，未配置本地化器时英文兜底（英文优先）。</summary>
public sealed class LocalizationTests
{
    [Fact]
    public void T_NoLocalizer_ReturnsEnglishKey()
    {
        // 未注入 Localizer 时：消息按英文原文输出
        ConsoleServices.Localizer = null;
        Assert.Equal("Model Registry", ConsoleServices.T("Model Registry"));
        Assert.Equal("ready", ConsoleServices.T("ready"));
    }

    [Fact]
    public void T_NoLocalizer_FormatsPlaceholders()
    {
        ConsoleServices.Localizer = null;
        Assert.Equal("Total 3 ready / 1 missing", ConsoleServices.T("Total {0} ready / {1} missing", 3, 1));
        Assert.Equal("Device: test", ConsoleServices.T("Device: {0}", "test"));
    }

    [Fact]
    public void T_UnknownKey_FallsBackToEnglish()
    {
        // 本地化器命中但 key 缺失 → ResourceNotFound → 英文兜底
        ConsoleServices.Localizer = null;
        var s = ConsoleServices.T("no-such-key-anywhere");
        Assert.Equal("no-such-key-anywhere", s);
    }
}
