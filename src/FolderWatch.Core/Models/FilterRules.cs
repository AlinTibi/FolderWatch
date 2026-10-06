namespace FolderWatch.App.Models;

public sealed class FilterRules
{
    public List<string> Include { get; init; } = new();
    public List<string> Exclude { get; init; } = new();
}

public sealed class ScanIssue
{
    public string RelativePath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public string Reason { get; init; } = string.Empty;
}
