namespace Centurion.Abstractions.Commands;

/// <summary>
/// Command request model containing a command name and normalized parameter dictionary.
/// CLI arguments and JSON files are normalized to this model. JSON-RPC (method=Name, params=Parameters)
/// and REST (POST /commands/{Name}) can reuse this contract without changing execution or validation.
/// </summary>
/// <param name="Name">Command name, such as "spawn" or "translate".</param>
/// <param name="Parameters">Normalized kebab-case keys mapped to CLR values or JSON elements.</param>
public sealed record CommandRequest(string Name, Dictionary<string, object?> Parameters);
