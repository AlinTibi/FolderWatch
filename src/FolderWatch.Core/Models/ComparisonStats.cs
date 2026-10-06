namespace FolderWatch.App.Models;

public sealed record ComparisonStats(int Total, int Added, int Removed, int Modified, int Unchanged, int Inaccessible)
{
    public static readonly ComparisonStats Empty = new(0, 0, 0, 0, 0, 0);
}
