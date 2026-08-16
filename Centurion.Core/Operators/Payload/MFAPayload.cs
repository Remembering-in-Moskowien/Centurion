namespace Centurion.Core.Operators.Payload;

public class MfaAlignPayload
{
    public required string AudioFilePath { get; set; }
    public required string Transcript { get; set; }
}

public class MfaWord
{
    public string Word { get; set; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
}