// Centurion.Core/Abstractions/IConfirmPrompt.cs

namespace Centurion.Core.Abstractions.Console;

public interface IConfirmPrompt
{
    Task<bool> ConfirmAsync(string prompt);
}