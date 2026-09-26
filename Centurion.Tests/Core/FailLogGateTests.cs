using Centurion.Abstractions.Utils;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>FailLogGate 使用进程级静态状态，测试须串行执行以避免相互竞争。</summary>
[CollectionDefinition("FailLogGate", DisableParallelization = true)]
public sealed class FailLogGateCollection;

[Collection("FailLogGate")]
public sealed class FailLogGateTests
{
    private sealed class FakeLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    public FailLogGateTests()
    {
        FailLogGate.Reset();
    }

    [Fact]
    public void FirstLog_IsError()
    {
        var logger = new FakeLogger();

        FailLogGate.Log(logger, "First failure {Code}", 1);

        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Contains("First failure 1", logger.Entries[0].Message);
        Assert.True(FailLogGate.HasFailed);
    }

    [Fact]
    public void SubsequentLogs_DowngradedToWarning()
    {
        var logger = new FakeLogger();

        FailLogGate.Log(logger, "Failure one");
        FailLogGate.Log(logger, "Failure two");
        FailLogGate.Log(logger, "Failure three");

        Assert.Equal(3, logger.Entries.Count);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Equal(LogLevel.Warning, logger.Entries[1].Level);
        Assert.Equal(LogLevel.Warning, logger.Entries[2].Level);
    }

    [Fact]
    public void ExceptionOverload_IsGatedAsWell()
    {
        var logger = new FakeLogger();
        var ex = new InvalidOperationException("boom");

        FailLogGate.Log(logger, ex, "First with exception");
        FailLogGate.Log(logger, ex, "Second with exception");

        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Equal(LogLevel.Warning, logger.Entries[1].Level);
    }

    [Fact]
    public void NullLogger_SilentlySkipped()
    {
        FailLogGate.Log(null, "No logger here");
        FailLogGate.Log(null, new InvalidOperationException(), "No logger here either");

        Assert.False(FailLogGate.HasFailed);
    }

    [Fact]
    public void Reset_AllowsNextFail()
    {
        var logger = new FakeLogger();

        FailLogGate.Log(logger, "First");
        FailLogGate.Reset();
        FailLogGate.Log(logger, "Second after reset");

        Assert.Equal(2, logger.Entries.Count);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Equal(LogLevel.Error, logger.Entries[1].Level);
    }
}
