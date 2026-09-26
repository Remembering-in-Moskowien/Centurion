namespace Centurion.Cli.Commands;

internal static class MediaFileExtensions
{
    public static readonly HashSet<string> VideoAndAudio =
    [
        ".mp3", ".wma", ".wav", ".flac", ".aac", ".ogg", ".ape", ".m4a", ".mka",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".mts", ".webm", ".flv",
        ".m2ts", ".mpeg", ".mpg", ".dv", ".rmvb", ".rm", ".asf", ".vob", ".ogv", ".mxf"
    ];

    public static readonly HashSet<string> OcrInputs =
    [.. VideoAndAudio, ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];
}