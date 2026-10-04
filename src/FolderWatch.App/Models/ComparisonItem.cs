namespace FolderWatch.App.Models;

public enum ChangeType
{
    Added,
    Removed,
    Modified,
    Unchanged
}

public sealed class ComparisonItem
{
    public string RelativePath { get; init; } = string.Empty;
    public ChangeType Change { get; init; }
    public long? OldSize { get; init; }
    public long? NewSize { get; init; }
}
