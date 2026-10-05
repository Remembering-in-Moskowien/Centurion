using System.Diagnostics;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Utils;
using Centurion.Cli.Commands.Settings;
using Centurion.Core.Utils.Reporting;
using Centurion.Core.Utils.Serialization;
using Centurion.Core.Workflow.Pipeline;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Models.Console;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Spectre.Console.Cli;
using Centurion.Cli;

namespace Centurion.Cli.Commands;

/// <summary>
/// <c>quality</c> command: intermediate file → quality report (.quality.json + .quality.html),
/// optional auto-fix (--fix) and CI threshold evaluation (--fail-on; exit code 1 on
/// any unmet rule). The evaluation result (Passed / FailedThresholds) is written back
/// to .quality.json for CI consumption.
/// </summary>
public sealed class QualityCommand(
    ITempDirectoryManager tempManager,
    PipelineExecutor pipelineExecutor,
    QualityReportOperator qualityReportOp,
    ICenturionDocumentStore store,
    ILogger<QualityCommand> logger) : AsyncCommand<QualitySettings>
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = [new StringEnumConverter()]
    };

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, QualitySettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.InputFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Input file not found: {inputPath}", inputPath);
            if (!CenturionFileIO.IsCenturionFile(inputPath))
                throw new InvalidDataException(
                    $"'{inputPath}' is not a Centurion intermediate file. Run 'Centurion convert <file>' first.");

            var outputPath = settings.OutputFile?.FullName
                ?? CenturionFileIO.DefaultOutputPath(inputPath, "quality");

            var loadedDoc = await store.LoadAsync(inputPath, ct);
            var workflowContext = new SubtitleWorkflowContext(loadedDoc.Config) { State = loadedDoc.State };
            workflowContext.Config.OutputFilePath = outputPath;

            await using var tempDir = await tempManager.CreateTempDirectoryAsync("quality_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            // 1) Generate the quality report (.quality.json and .quality.html).
            var stopwatch = Stopwatch.StartNew();
            await pipelineExecutor.ExecuteAsync([qualityReportOp], workflowContext, ct);
            stopwatch.Stop();
            var report = QualityReportBuilder.Build(workflowContext, outputPath, stopwatch.Elapsed.TotalSeconds);

            // 2) Apply automatic fixes for overlaps, short lines, line width, and CPS; persist the corrected intermediate file.
            var appliedFixes = new List<string>();
            var skippedFixes = new List<string>();
            if (settings.Fix)
            {
                var sentences = QualityReportBuilder.EffectiveSentences(workflowContext.State);
                var fixResult = QualityFixer.Fix(sentences);
                workflowContext.State.CurrentSentences = fixResult.Sentences;
                appliedFixes = fixResult.Applied;
                skippedFixes = fixResult.Skipped;

                if (appliedFixes.Count > 0)
                {
                    // Rebuild the report if fixes changed sentences, replacing the operator-generated files.
                    report = QualityReportBuilder.Build(workflowContext, outputPath, stopwatch.Elapsed.TotalSeconds);
                }
            }

            // Save the intermediate file with corrected sentences, or copy it unchanged when no fixes were applied.
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "quality", outputPath);
            await store.SaveAsync(outDoc, outputPath, ct);

            // 3) Evaluate CI thresholds; true means a limit was exceeded, and any exceeded limit fails the command.
            var rules = new List<QualityThresholdRule>();
            foreach (var expr in settings.FailOn)
            {
                var rule = QualityThresholdRule.TryParse(expr);
                if (rule is null)
                    throw new InvalidDataException(
                        $"Invalid --fail-on rule '{expr}'. Expected forms like 'cps>20', 'coverage<95', 'maxcps<=18'.");
                rules.Add(rule);
            }
            report.Passed = true;
            report.FailedThresholds.Clear();
            foreach (var rule in rules)
            {
                if (rule.Evaluate(report))
                {
                    report.Passed = false;
                    report.FailedThresholds.Add($"{rule.Expression} (actual {DescribeActual(rule, report)})");
                }
            }

            // 4) Write the final report; JSON includes Passed/FailedThresholds and HTML contains the same data.
            var reportPath = BuildReportPath(inputPath, outputPath);
            File.WriteAllText(reportPath, JsonConvert.SerializeObject(report, SerializerSettings));
            var htmlPath = settings.HtmlFile?.FullName ?? Path.ChangeExtension(reportPath, ".html");
            File.WriteAllText(htmlPath, QualityHtmlReport.Render(report));

            // 5) Print a summary.
            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Quality report written to {0}", reportPath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T(
                "Sentences: {0}, issues: {1} (error {2} / warning {3}), HTML: {4}",
                report.Counts.SentenceCount, report.Issues.Count,
                report.Issues.Count(i => i.Severity == QualityIssueSeverity.Error.ToString()),
                report.Issues.Count(i => i.Severity == QualityIssueSeverity.Warning.ToString()),
                htmlPath));

            if (appliedFixes.Count > 0)
            {
                ConsoleServices.Output.WriteInfo(ConsoleServices.T(
                    "Auto-fix applied {0} change(s); fixed intermediate: {1}", appliedFixes.Count, outputPath));
                foreach (var fix in appliedFixes.Take(10))
                    ConsoleServices.Output.WriteLine($"  - {fix}");
                if (appliedFixes.Count > 10)
                    ConsoleServices.Output.WriteLine($"  … and {appliedFixes.Count - 10} more");
            }
            foreach (var skip in skippedFixes.Take(5))
                ConsoleServices.Output.WriteLine($"  (skipped) {skip}");

            if (!report.Passed)
            {
                ConsoleServices.Output.WriteError(ConsoleServices.T(
                    "Quality gate failed: {0}", string.Join("; ", report.FailedThresholds)));
                return ExitCodes.Failure;
            }

            return 0;
        }
        catch (Exception ex)
        {
            CliErrorPrinter.Print(logger, ex, "quality execution failed.");
            return ExitCodes.Failure;
        }
    }

    /// <summary>
    /// Report path = <c>&lt;stem&gt;.quality.json</c> next to the input file
    /// (stem drops the .centurion.json suffix from the input name); independent of the IR path.
    /// </summary>
    private static string BuildReportPath(string inputPath, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        if (stem.EndsWith(".centurion", StringComparison.OrdinalIgnoreCase))
            stem = stem[..^".centurion".Length];
        return Path.Combine(dir, stem + ".quality.json");
    }

    private static string DescribeActual(QualityThresholdRule rule, QualityReport report)
        => rule.Evaluate(report) ? "failed" : "ok";
}
