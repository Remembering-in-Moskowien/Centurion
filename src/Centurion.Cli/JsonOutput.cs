using System.Text.Json;

namespace Centurion.Cli;

/// <summary>
/// Machine-readable JSON output port: writes straight to stdout (bypassing Spectre
/// rendering/log prefixes) so scripts can safely consume <c>--json</c> output.
/// Callers must ensure normal log lines are suppressed (Program raises the log level
/// to Warning when --json is set).
/// </summary>
public static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Serializes any object to JSON and writes it to stdout (plain line, no trailing newline).</summary>
    public static void Write(object payload)
    {
        System.Console.Out.WriteLine(JsonSerializer.Serialize(payload, payload.GetType(), Options));
    }
}
