using Centurion.Cli.Console;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Centurion.Tests.Console;

public sealed class SpectreConsoleOutputTests
{
    [Fact]
    public void WriteMarkupLine_WhenPlainTextContainsSquareBrackets_DoesNotThrow()
    {
        var output = new SpectreConsoleOutput(NullLogger<SpectreConsoleOutput>.Instance);
        var previous = SpectreConsoleOutput.SuppressHumanLines;
        SpectreConsoleOutput.SuppressHumanLines = false;

        try
        {
            var exception = Record.Exception(() => output.WriteMarkupLine("literal [brackets] should stay visible"));
            Assert.Null(exception);
        }
        finally
        {
            SpectreConsoleOutput.SuppressHumanLines = previous;
        }
    }
}
