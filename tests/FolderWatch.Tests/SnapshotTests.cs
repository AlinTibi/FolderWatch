using System.Text.Json;
using FolderWatch.App.Models;
using FolderWatch.App.Services;
using Xunit;

namespace FolderWatch.Tests;

public sealed class SnapshotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FolderWatch-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SnapshotService _scanner = new();
    public SnapshotTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private string Write(string name, string contents = "first")
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact] public async Task IdenticalFolderIsUnchanged()
    {
        Write("one.txt");
        var before = await _scanner.CreateAsync(_root);
        var after = await _scanner.CreateAsync(_root);
        Assert.Equal(ChangeType.Unchanged, Assert.Single(new CompareService().Compare(before, after)).Change);
    }
    [Fact] public async Task AddedFile()
    {
        var before = await _scanner.CreateAsync(_root); Write("new.txt");
        Assert.Equal(ChangeType.Added, Assert.Single(new CompareService().Compare(before, await _scanner.CreateAsync(_root))).Change);
    }
    [Fact] public async Task RemovedFile()
    {
        var file = Write("gone.txt"); var before = await _scanner.CreateAsync(_root); File.Delete(file);
        Assert.Equal(ChangeType.Removed, Assert.Single(new CompareService().Compare(before, await _scanner.CreateAsync(_root))).Change);
    }
    [Fact] public async Task HashDetectsSameSizeAndTimestampContentChange()
    {
        var file = Write("changed.txt", "aaaa"); var before = await _scanner.CreateAsync(_root);
        var timestamp = File.GetLastWriteTimeUtc(file); File.WriteAllText(file, "bbbb"); File.SetLastWriteTimeUtc(file, timestamp);
        var after = await _scanner.CreateAsync(_root);
        Assert.Equal(before.Files[0].Size, after.Files[0].Size);
        Assert.NotEqual(before.Files[0].Sha256, after.Files[0].Sha256);
        Assert.Equal(ChangeType.Modified, Assert.Single(new CompareService().Compare(before, after)).Change);
    }
    [Fact] public async Task LockedFileIsInaccessibleNotRemovedAndScanContinues()
    {
        var file = Write("locked.txt"); Write("readable.txt"); var before = await _scanner.CreateAsync(_root);
        using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var after = await _scanner.CreateAsync(_root);
        Assert.True(after.IsPartial); Assert.Single(after.Files); Assert.Single(after.Issues);
        var results = new CompareService().Compare(before, after);
        Assert.Equal(ChangeType.Inaccessible, results.Single(r => r.RelativePath == "locked.txt").Change);
        Assert.Equal(ChangeType.Unchanged, results.Single(r => r.RelativePath == "readable.txt").Change);
        Assert.Contains("locked", after.Issues[0].Reason);
    }
    private sealed class FailingHasher(Action<string> action, Exception? failure = null) : IFileHasher
    {
        public async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
        {
            action(path); if (failure is not null) throw failure;
            return await new HashService().ComputeSha256Async(path, cancellationToken);
        }
    }
    [Fact] public async Task AccessDeniedIsRecordedWithoutAborting()
    {
        Write("denied.txt");
        var snapshot = await new SnapshotService(new FailingHasher(_ => { }, new UnauthorizedAccessException())).CreateAsync(_root);
        Assert.Empty(snapshot.Files); Assert.True(snapshot.IsPartial); Assert.Contains("Access denied", Assert.Single(snapshot.Issues).Reason);
    }
    [Fact] public async Task DisappearanceDuringHashIsRecorded()
    {
        Write("gone.txt");
        var snapshot = await new SnapshotService(new FailingHasher(File.Delete)).CreateAsync(_root);
        Assert.Empty(snapshot.Files); Assert.Contains("disappeared", Assert.Single(snapshot.Issues).Reason);
    }
    [Fact] public async Task ChangeDuringHashRejectsInconsistentCapture()
    {
        Write("moving.txt");
        var snapshot = await new SnapshotService(new FailingHasher(p => File.AppendAllText(p, "more"))).CreateAsync(_root);
        Assert.Empty(snapshot.Files); Assert.Contains("changed", Assert.Single(snapshot.Issues).Reason);
    }
    [Fact] public async Task CancellationDoesNotReturnCompleteSnapshot()
    {
        Write("one.txt"); using var cancellation = new CancellationTokenSource();
        var scanner = new SnapshotService(new FailingHasher(_ => cancellation.Cancel()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.CreateAsync(_root, cancellationToken: cancellation.Token));
    }
    [Fact] public async Task CancelledSavePreservesExistingFile()
    {
        var destination = Write("existing.folderwatch.json", "original");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _scanner.SaveAsync(new FolderSnapshot(), destination, cancellation.Token));
        Assert.Equal("original", File.ReadAllText(destination));
        Assert.Single(Directory.GetFiles(_root));
    }
    [Fact] public async Task ExcludedDirectoriesAndFilesDoNotEnterSnapshot()
    {
        Write("bin/compiled.txt"); Write("nested/bin/compiled.txt"); Write("one.tmp"); Write("nested/keep.txt"); Write("one.log");
        var snapshot = await _scanner.CreateAsync(_root, rules: new FilterRules { Exclude = new() { "bin", "*.tmp", "*.log" } });
        Assert.Equal(Path.Combine("nested", "keep.txt"), Assert.Single(snapshot.Files).RelativePath);
        Assert.False(snapshot.IsPartial);
    }
    [Fact] public async Task IncludeDoesNotPruneMatchingDescendants()
    {
        Write("deep/nested/yes.json"); Write("deep/no.txt");
        var snapshot = await _scanner.CreateAsync(_root, rules: new FilterRules { Include = new() { "**/*.json" } });
        Assert.EndsWith("yes.json", Assert.Single(snapshot.Files).RelativePath);
    }
    [Theory]
    [InlineData("*.tmp", "nested/a.tmp", true)]
    [InlineData("*.tmp", "nested/a.txt", false)]
    [InlineData("file?.txt", "nested/file1.txt", true)]
    [InlineData("file?.txt", "file12.txt", false)]
    [InlineData("src/*.cs", "src/nested/a.cs", false)]
    [InlineData("src/**", "src/nested/a.cs", true)]
    [InlineData("src/**", "src", true)]
    [InlineData("src/**", "nested/src/a.cs", false)]
    [InlineData("**/*.json", "one.json", true)]
    [InlineData("bin", "nested/bin/output.dll", true)]
    [InlineData(".git", "project/.git/config", true)]
    [InlineData("Thumbs.db", "THUMBS.DB", true)]
    public void GlobRules(string pattern, string path, bool excluded) =>
        Assert.Equal(excluded, new FilterMatcher(new FilterRules { Exclude = new() { pattern } }).Excludes(path, false));

    [Theory] [InlineData("../secret")] [InlineData("C:/Windows")] [InlineData("/absolute")]
    public void InvalidPatternsRejected(string pattern) => Assert.Throws<InvalidDataException>(() => new FilterMatcher(new FilterRules { Exclude = new() { pattern } }));

    [Fact] public async Task SchemaTwoRulesAndIssuesRoundTrip()
    {
        var snapshot = new FolderSnapshot { SchemaVersion = 2, Rules = new FilterRules { Include = new() { "*.txt" }, Exclude = new() { "bin" } },
            Issues = new() { new ScanIssue { RelativePath = "locked.txt", Reason = "locked" } } };
        var path = Path.Combine(_root, "roundtrip.folderwatch.json"); await _scanner.SaveAsync(snapshot, path);
        var loaded = await _scanner.LoadAsync(path);
        Assert.Equal(2, loaded.SchemaVersion); Assert.True(loaded.IsPartial); Assert.True(FilterMatcher.Equivalent(snapshot.Rules, loaded.Rules));
        Assert.Equal("locked", Assert.Single(loaded.Issues).Reason);
    }
    [Fact] public async Task OldSnapshotWithoutNewFieldsStillCompares()
    {
        Write("one.txt"); var current = await _scanner.CreateAsync(_root);
        var legacy = JsonSerializer.Serialize(new { current.RootPath, current.CreatedUtc, current.Files });
        var path = Write("old.folderwatch.json", legacy); var old = await _scanner.LoadAsync(path);
        Assert.Equal(1, old.SchemaVersion); Assert.Empty(old.Rules.Exclude); Assert.False(old.IsPartial);
        Assert.Equal(ChangeType.Unchanged, Assert.Single(new CompareService().Compare(old, await _scanner.CreateAsync(_root))).Change);
    }
    [Theory] [InlineData(0)] [InlineData(3)]
    public async Task UnknownSchemaRejected(int version)
    {
        var path = Write("bad.folderwatch.json", "{\"SchemaVersion\":" + version + "}");
        await Assert.ThrowsAsync<InvalidDataException>(() => _scanner.LoadAsync(path));
    }
    [Fact] public async Task DuplicatePathsRejected()
    {
        var entry = new SnapshotFile { RelativePath = "a.txt", Sha256 = new string('A', 64) };
        var path = Write("bad.folderwatch.json", JsonSerializer.Serialize(new FolderSnapshot { Files = new() { entry, entry } }));
        await Assert.ThrowsAsync<InvalidDataException>(() => _scanner.LoadAsync(path));
    }
    [Fact] public void DirectoryFailureProtectsDescendantsWithoutPrefixFalseMatches()
    {
        var old = new FolderSnapshot { Files = new() { FileEntry("secret/a.txt"), FileEntry("secretary/a.txt") } };
        var current = new FolderSnapshot { Issues = new() { new ScanIssue { RelativePath = "secret", IsDirectory = true, Reason = "Access denied" } } };
        var rows = new CompareService().Compare(old, current);
        Assert.Equal(ChangeType.Inaccessible, rows.Single(r => r.RelativePath == "secret/a.txt").Change);
        Assert.Equal(ChangeType.Removed, rows.Single(r => r.RelativePath == "secretary/a.txt").Change);
    }
    [Fact] public void PartialBaselineDoesNotInventAddedFiles()
    {
        var old = new FolderSnapshot { Issues = new() { new ScanIssue { RelativePath = "secret", IsDirectory = true, Reason = "Access denied" } } };
        var current = new FolderSnapshot { Files = new() { FileEntry("secret/a.txt") } };
        Assert.Equal(ChangeType.Inaccessible, new CompareService().Compare(old, current).Single(r => r.RelativePath == "secret/a.txt").Change);
    }
    [Fact] public void MismatchedScopeCannotProduceDiff() => Assert.Throws<InvalidOperationException>(() => new CompareService().Compare(
        new FolderSnapshot { Rules = new FilterRules { Exclude = new() { "*.tmp" } } }, new FolderSnapshot()));
    [Fact] public async Task CsvEscapesAndUsesFilteredRows()
    {
        var path = Path.Combine(_root, "report.csv");
        var rows = new[] { new ComparisonItem { Change = ChangeType.Inaccessible, RelativePath = "a,\"b.txt", Reason = "line1\nline2", OldSize = 12 },
            new ComparisonItem { Change = ChangeType.Unchanged, RelativePath = "omit.txt" } };
        await new CsvExportService().ExportAsync(CompareService.ApplyFilter(rows, ResultFilter.Inaccessible), path);
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("Old Modified,New Modified,Reason", text); Assert.Contains("\"a,\"\"b.txt\"", text);
        Assert.Contains("\"line1\nline2\"", text); Assert.DoesNotContain("omit.txt", text);
        Assert.Equal(1, CompareService.ComputeStats(rows).Inaccessible);
    }
    private static SnapshotFile FileEntry(string path) => new() { RelativePath = path, Sha256 = new string('A', 64) };

    [Fact] public async Task ReparseDirectoryLoopIsSkipped()
    {
        Write("one.txt");
        var link = Path.Combine(_root, "loop");
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(_root);
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, _root);
        var snapshot = await _scanner.CreateAsync(_root);
        Assert.Single(snapshot.Files); Assert.True(snapshot.IsPartial);
        Assert.Contains("Reparse point", Assert.Single(snapshot.Issues).Reason);
        Directory.Delete(link);
    }
    [Fact] public async Task LongRelativePathsCanBeCaptured()
    {
        var path = string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat(new string('a', 45), 6)) + Path.DirectorySeparatorChar + "long.txt";
        Write(path);
        Assert.Equal(path, Assert.Single((await _scanner.CreateAsync(_root)).Files).RelativePath);
    }
    [Fact] public void RootFailureProtectsAllMissingFiles()
    {
        var before = new FolderSnapshot { Files = new() { FileEntry("one.txt"), FileEntry("nested/two.txt") } };
        var after = new FolderSnapshot { Issues = new() { new ScanIssue { RelativePath = ".", IsDirectory = true, Reason = "I/O error" } } };
        Assert.All(new CompareService().Compare(before, after), row => Assert.Equal(ChangeType.Inaccessible, row.Change));
    }
    [Fact] public void SuccessfulCaptureOverridesPartialDirectoryIssue()
    {
        var before = new FolderSnapshot { Files = new() { FileEntry("nested/ok.txt") } };
        var after = new FolderSnapshot { Files = new() { FileEntry("nested/ok.txt") }, Issues = new() { new ScanIssue { RelativePath = "nested", IsDirectory = true, Reason = "I/O error" } } };
        Assert.Equal(ChangeType.Unchanged, new CompareService().Compare(before, after).Single(r => r.RelativePath == "nested/ok.txt").Change);
    }
    [Fact] public async Task NullCollectionsAreRejectedGracefully()
    {
        var path = Write("bad.folderwatch.json", "{\"Files\":null}");
        await Assert.ThrowsAsync<InvalidDataException>(() => _scanner.LoadAsync(path));
    }
    [Theory]
    [InlineData(ResultFilter.Added, ChangeType.Added)]
    [InlineData(ResultFilter.Removed, ChangeType.Removed)]
    [InlineData(ResultFilter.Modified, ChangeType.Modified)]
    [InlineData(ResultFilter.Unchanged, ChangeType.Unchanged)]
    [InlineData(ResultFilter.Inaccessible, ChangeType.Inaccessible)]
    public void EveryStatusFilterKeepsOnlyThatStatus(ResultFilter filter, ChangeType expected)
    {
        var rows = Enum.GetValues<ChangeType>().Select(c => new ComparisonItem { Change = c }).ToList();
        Assert.Equal(expected, Assert.Single(CompareService.ApplyFilter(rows, filter)).Change);
        Assert.Equal(5, CompareService.ApplyFilter(rows, ResultFilter.All).Count);
    }
}
