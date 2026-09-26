using Centurion.Core.Workflow.DependencyInjection;using Centurion.Core.Workflow.Factories;using Centurion.Models.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Centurion.Tests.Core;

public sealed class PipelineOperatorFactoryTests
{
    [Fact]
    public void CreateTranscribeOperator_UnsupportedEngine_ThrowsDuringAssembly()
    {
        using var provider = CreateProvider();
        var factory = provider.GetRequiredService<PipelineOperatorFactory>();

        Assert.Throws<NotSupportedException>(() => factory.CreateTranscribeOperator(
            new WorkflowConfig { TranscriberEngine = "invalid-engine" }));
    }

    [Fact]
    public void CreateSentenceSplitOperator_UnsupportedStrategy_ThrowsDuringAssembly()
    {
        using var provider = CreateProvider();
        var factory = provider.GetRequiredService<PipelineOperatorFactory>();

        Assert.Throws<NotSupportedException>(() => factory.CreateSentenceSplitOperator(
            new WorkflowConfig { SplitStrategy = "invalid-strategy" }));
    }

    [Fact]
    public void CreateDiarizationOperator_UnsupportedBackend_ThrowsDuringAssembly()
    {
        using var provider = CreateProvider();
        var factory = provider.GetRequiredService<PipelineOperatorFactory>();

        Assert.Throws<NotSupportedException>(() => factory.CreateDiarizationOperator(
            new WorkflowConfig { DiarizationBackend = "invalid-backend" }));
    }

    [Fact]
    public void CreateDiarizationOperator_NoneBackend_OmitsStage()
    {
        using var provider = CreateProvider();
        var factory = provider.GetRequiredService<PipelineOperatorFactory>();

        Assert.Null(factory.CreateDiarizationOperator(new WorkflowConfig { DiarizationBackend = "none" }));
    }

    [Fact]
    public void CreateAlignmentOperator_UnregisteredModel_ThrowsDuringAssembly()
    {
        using var provider = CreateProvider();
        var factory = provider.GetRequiredService<PipelineOperatorFactory>();

        Assert.Throws<NotSupportedException>(() => factory.CreateAlignmentOperator(
            new WorkflowConfig { EnableAlignment = true, AlignmentModel = "invalid-model" }));
    }

    [Fact]
    public void CreateAlignmentOperator_Disabled_OmitsStage()
    {
        using var provider = CreateProvider();
        var factory = provider.GetRequiredService<PipelineOperatorFactory>();

        Assert.Null(factory.CreateAlignmentOperator(new WorkflowConfig { EnableAlignment = false }));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCenturionCore();
        return services.BuildServiceProvider();
    }
}