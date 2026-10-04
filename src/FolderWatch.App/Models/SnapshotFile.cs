namespace FolderWatch.App.Models;

public sealed class SnapshotFile
{
    public string RelativePath { get; init; } = string.Empty;
    public long Size { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}
