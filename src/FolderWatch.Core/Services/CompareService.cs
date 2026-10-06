using FolderWatch.App.Models;

namespace FolderWatch.App.Services;

public sealed class CompareService
{
    public IReadOnlyList<ComparisonItem> Compare(FolderSnapshot oldSnapshot, FolderSnapshot newSnapshot)
    {
        if (!FilterMatcher.Equivalent(oldSnapshot.Rules, newSnapshot.Rules))
            throw new InvalidOperationException("Filter rules differ. Scan again using the snapshot rules.");
        var oldFiles = oldSnapshot.Files.ToDictionary(x => SnapshotService.Key(x.RelativePath), StringComparer.OrdinalIgnoreCase);
        var newFiles = newSnapshot.Files.ToDictionary(x => SnapshotService.Key(x.RelativePath), StringComparer.OrdinalIgnoreCase);
        var oldIssues = IndexIssues(oldSnapshot);
        var newIssues = IndexIssues(newSnapshot);
        var paths = oldFiles.Keys.Union(newFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(oldIssues.Keys, StringComparer.OrdinalIgnoreCase).Union(newIssues.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        var results = new List<ComparisonItem>();
        foreach (var path in paths)
        {
            oldFiles.TryGetValue(path, out var oldFile);
            newFiles.TryGetValue(path, out var newFile);
            var currentIssue = FindIssue(newIssues, path);
            var previousIssue = FindIssue(oldIssues, path);
            var reason = newFile is null && currentIssue is not null ? currentIssue.Reason :
                oldFile is null && previousIssue is not null ? "Previous snapshot: " + previousIssue.Reason : string.Empty;
            var change = reason.Length > 0 ? ChangeType.Inaccessible : oldFile is null ? ChangeType.Added :
                newFile is null ? ChangeType.Removed : string.Equals(oldFile.Sha256, newFile.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? ChangeType.Unchanged : ChangeType.Modified;
            results.Add(new ComparisonItem { RelativePath = oldFile?.RelativePath ?? newFile?.RelativePath ?? path,
                Change = change, OldSize = oldFile?.Size, NewSize = newFile?.Size,
                OldModified = oldFile?.LastWriteTimeUtc, NewModified = newFile?.LastWriteTimeUtc, Reason = reason });
        }
        return results;
    }

    private static Dictionary<string, ScanIssue> IndexIssues(FolderSnapshot snapshot) => snapshot.Issues
        .GroupBy(i => SnapshotService.Key(i.RelativePath), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IsDirectory).First(), StringComparer.OrdinalIgnoreCase);
    private static ScanIssue? FindIssue(Dictionary<string, ScanIssue> issues, string path)
    {
        if (issues.TryGetValue(path, out var exact)) return exact;
        var slash = path.LastIndexOf('/');
        while (slash > 0)
        {
            if (issues.TryGetValue(path[..slash], out var ancestor) && ancestor.IsDirectory) return ancestor;
            slash = path.LastIndexOf('/', slash - 1);
        }
        return issues.TryGetValue(".", out var root) && root.IsDirectory ? root : null;
    }

    public static IReadOnlyList<ComparisonItem> ApplyFilter(IReadOnlyList<ComparisonItem> results, ResultFilter filter)
    {
        if (filter == ResultFilter.All) return results;
        var change = ToChangeType(filter);
        return results.Where(x => x.Change == change).ToList();
    }
    private static ChangeType ToChangeType(ResultFilter filter) => filter switch
    {
        ResultFilter.Added => ChangeType.Added, ResultFilter.Removed => ChangeType.Removed,
        ResultFilter.Modified => ChangeType.Modified, ResultFilter.Unchanged => ChangeType.Unchanged,
        ResultFilter.Inaccessible => ChangeType.Inaccessible,
        _ => throw new ArgumentOutOfRangeException(nameof(filter))
    };
    public static ComparisonStats ComputeStats(IReadOnlyList<ComparisonItem> results)
    {
        var counts = new int[5];
        foreach (var item in results) counts[(int)item.Change]++;
        return new ComparisonStats(results.Count, counts[0], counts[1], counts[2], counts[3], counts[4]);
    }
}
