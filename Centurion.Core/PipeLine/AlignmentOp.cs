// File: Centurion.Core.Operators/AlignmentOperator.cs

using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Managers;
using Centurion.Core.Models;
using Centurion.Core.Strategy.Alignment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.PipeLine
{
    /// <summary>
    /// Pipeline operator that performs forced alignment on sentences.
    /// It reads the split sentences and converted audio path from the workflow context,
    /// executes the configured alignment strategy (e.g., CrispASR) to refine word-level timestamps,
    /// and writes the result back to State.AlignedSentences.
    /// </summary>
    public class AlignmentOp : PipelineOperatorBase, IHealthCheckableOperator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IModelPathResolver _modelPathResolver;
        private readonly ILogger<AlignmentOp> _logger;

        public override string Name => "Forced Alignment";

        public AlignmentOp(
            IServiceProvider serviceProvider,
            IModelPathResolver modelPathResolver,
            ILogger<AlignmentOp> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _modelPathResolver = modelPathResolver ?? throw new ArgumentNullException(nameof(modelPathResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
        {
            // 1. Check if alignment is enabled in configuration
            if (!context.Config.EnableAlignment)
            {
                LogInfo("Alignment is disabled (EnableAlignment=false). Skipping.");
                context.State.AlignedSentences = context.State.SplitSentences ?? new List<Sentence>();
                context.State.IsAligned = true;
                return;
            }

            // 2. Validate input data
            var sentences = context.State.SplitSentences;
            if (sentences == null || sentences.Count == 0)
            {
                LogWarning("No sentences to align. Skipping alignment.");
                context.State.AlignedSentences = new List<Sentence>();
                context.State.IsAligned = true;
                return;
            }

            var audioPath = context.State.ConvertedAudioPath;
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
            {
                throw new InvalidOperationException($"Audio file not found or not converted: {audioPath}");
            }

            // 3. Report progress: start
            OnProgress(0, "Preparing alignment...");

            // 4. Resolve the appropriate alignment strategy based on configuration
            if (context.Config.AlignmentModel != null)
            {
                IAlignmentStrategy strategy = ResolveStrategy(context.Config.AlignmentModel);

                // 5. Execute alignment
                LogInfo($"Using alignment model: {context.Config.AlignmentModel}");
                OnProgress(30, "Running alignment...");

                var alignedSentences = await strategy.AlignAsync(sentences, audioPath, cancellationToken);

                // 6. Write back to context
                context.State.AlignedSentences = alignedSentences ?? new List<Sentence>();
                context.State.IsAligned = true;

                // 7. Report completion
                OnProgress(100, "Alignment completed");
                if (alignedSentences != null)
                    LogInfo($"Alignment finished. Processed {alignedSentences.Count} sentences.");
            }
        }

        /// <summary>
        /// Resolves the appropriate IAlignmentStrategy implementation based on the model name.
        /// If a strategy is already registered in DI, it will be used; otherwise, a default
        /// strategy (CrispASR) is instantiated.
        /// </summary>
        private IAlignmentStrategy ResolveStrategy(string modelName)
        {
            // Option 1: Use a pre-registered strategy from the DI container (preferred)
            var strategy = _serviceProvider.GetService<IAlignmentStrategy>();
            if (strategy != null)
                return strategy;

            // Option 2: Fallback to CrispASR (default) using the given model name
            var logger = _serviceProvider.GetRequiredService<ILogger<CrispAsrAlignmentStrategy>>();
            var resolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
            return new CrispAsrAlignmentStrategy(resolver, _serviceProvider, logger, modelName);
        }

        /// <summary>
        /// Health check: verifies that the required external tool (CrispASR) exists.
        /// Model existence is verified during execution to avoid dependency on runtime context.
        /// </summary>
        public override async Task CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            LogInfo("Checking alignment environment...");

            // 1. Check if the CrispASR tool is installed (or any other external dependency)
            var toolManager = new ToolManager("crispasr", _serviceProvider);
            if (!File.Exists(toolManager.ExecutablePath))
            {
                LogWarning($"CrispASR tool not found at: {toolManager.ExecutablePath}");
            }
            else
            {
                LogInfo($"CrispASR tool found at: {toolManager.ExecutablePath}");
            }

            // 2. Optionally, check for other global dependencies (e.g., FFmpeg) if needed.
            // Model files are not checked here because the model name is runtime-specific
            // and is validated inside ExecuteAsync.

            await Task.CompletedTask;
        }
    }
}