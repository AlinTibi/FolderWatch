using FolderWatch.App.Models;

namespace FolderWatch.App.Services;

public sealed class CompareService
{
    public IReadOnlyList<ComparisonItem> Compare(FolderSnapshot oldSnapshot, FolderSnapshot newSnapshot)
    {
        var oldFiles = oldSnapshot.Files.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
        var newFiles = newSnapshot.Files.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
        var paths = oldFiles.Keys.Union(newFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        var results = new List<ComparisonItem>();

        foreach (var path in paths)
        {
            oldFiles.TryGetValue(path, out var oldFile);
            newFiles.TryGetValue(path, out var newFile);

            var change = oldFile is null
                ? ChangeType.Added
                : newFile is null
                    ? ChangeType.Removed
                    : string.Equals(oldFile.Sha256, newFile.Sha256, StringComparison.OrdinalIgnoreCase)
                        ? ChangeType.Unchanged
                        : ChangeType.Modified;

            results.Add(new ComparisonItem
            {
                RelativePath = path,
                Change = change,
                OldSize = oldFile?.Size,
                NewSize = newFile?.Size
            });
        }

        return results;
    }

    public static IReadOnlyList<ComparisonItem> ApplyFilter(IReadOnlyList<ComparisonItem> results, ResultFilter filter)
    {
        if (filter == ResultFilter.All)
        {
            return results;
        }

        var changeType = ToChangeType(filter);
        return results.Where(x => x.Change == changeType).ToList();
    }

    public static ComparisonStats ComputeStats(IReadOnlyList<ComparisonItem> results)
    {
        return new ComparisonStats(
            Total: results.Count,
            Added: results.Count(x => x.Change == ChangeType.Added),
            Removed: results.Count(x => x.Change == ChangeType.Removed),
            Modified: results.Count(x => x.Change == ChangeType.Modified),
            Unchanged: results.Count(x => x.Change == ChangeType.Unchanged));
    }

    private static ChangeType ToChangeType(ResultFilter filter) => filter switch
    {
        ResultFilter.Added => ChangeType.Added,
        ResultFilter.Removed => ChangeType.Removed,
        ResultFilter.Modified => ChangeType.Modified,
        ResultFilter.Unchanged => ChangeType.Unchanged,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Filter does not map to a change type.")
    };
}
