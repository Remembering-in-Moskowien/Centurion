namespace Centurion.Abstractions.Strategy;

using Centurion.Models.Asr;

/// <summary>
/// Cloud ASR connection settings: provider, API key, and endpoint. Local transcription strategies do not use these settings.
/// </summary>
/// <param name="Provider">Cloud ASR provider.</param>
/// <param name="ApiKey">Provider API key.</param>
/// <param name="BaseUrl">Custom endpoint; null uses the provider default.</param>
public sealed record AsrOptions(AsrProvider Provider, string? ApiKey, string? BaseUrl);
