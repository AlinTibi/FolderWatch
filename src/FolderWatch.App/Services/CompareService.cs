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
}
