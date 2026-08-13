// Centurion.Core/Abstractions/IConfirmPrompt.cs
namespace Centurion.Core.Abstractions;

public interface IConfirmPrompt
{
    Task<bool> ConfirmAsync(string prompt);
}