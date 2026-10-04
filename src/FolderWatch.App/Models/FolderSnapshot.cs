namespace FolderWatch.App.Models;

public sealed class FolderSnapshot
{
    public string RootPath { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public List<SnapshotFile> Files { get; init; } = new();
}
