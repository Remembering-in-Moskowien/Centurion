using Centurion.Models.Console;
using Xunit;

namespace Centurion.Tests.Cli;

/// <summary>Localization contract: keys are the English defaults, and English is the fallback when no localizer is configured (English first).</summary>
public sealed class LocalizationTests
{
    [Fact]
    public void T_NoLocalizer_ReturnsEnglishKey()
    {
        // When no localizer is injected: messages are output as the original English text.
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
        // When the localizer is hit but the key is missing, ResourceNotFound falls back to English.
        ConsoleServices.Localizer = null;
        var s = ConsoleServices.T("no-such-key-anywhere");
        Assert.Equal("no-such-key-anywhere", s);
    }
}
