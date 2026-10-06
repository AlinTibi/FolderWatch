namespace FolderWatch.App.Models;

public sealed class FolderSnapshot
{
    // Missing in v1.0.0 files: treat those as schema 1, never assume v2 rules.
    public int SchemaVersion { get; init; } = 1;
    public string RootPath { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public List<SnapshotFile> Files { get; init; } = new();
    public FilterRules Rules { get; init; } = new();
    public List<ScanIssue> Issues { get; init; } = new();
    public bool IsPartial => Issues.Count > 0;
}
