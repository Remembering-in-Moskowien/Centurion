namespace Centurion.Core.Exceptions;

/// <summary>外部二进制程序未找到统一异常</summary>
public class BinaryNotFoundException(string message, string binaryName) : Exception(message)
{
    public string BinaryName { get; } =  binaryName;
}