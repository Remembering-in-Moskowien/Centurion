// Centurion.Core/Abstractions/IConsoleOutput.cs
namespace Centurion.Core.Abstractions;

public interface IConsoleOutput
{
    void Write(string message);
    void WriteLine(string message);
    void WriteError(string message);
    void WriteWarning(string message);
    void WriteSuccess(string message);
    void WriteInfo(string message);
    void WriteMarkup(string markup);      // 支持颜色标记
    void WriteMarkupLine(string markup);
}