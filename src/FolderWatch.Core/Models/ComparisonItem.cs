namespace FolderWatch.App.Models;

public enum ChangeType
{
    Added,
    Removed,
    Modified,
    Unchanged,
    Inaccessible
}

public sealed class ComparisonItem
{
    public string RelativePath { get; init; } = string.Empty;
    public ChangeType Change { get; init; }
    public long? OldSize { get; init; }
    public long? NewSize { get; init; }
    public DateTime? OldModified { get; init; }
    public DateTime? NewModified { get; init; }
    public string Reason { get; init; } = string.Empty;
}
