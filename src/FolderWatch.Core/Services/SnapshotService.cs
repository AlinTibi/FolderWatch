using System.Text.Json;
using FolderWatch.App.Models;

namespace FolderWatch.App.Services;

public sealed class SnapshotService
{
    public const string SnapshotFileSuffix = ".folderwatch.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly IFileHasher _hasher;
    public SnapshotService(IFileHasher? hasher = null) => _hasher = hasher ?? new HashService();

    public Task<FolderSnapshot> CreateAsync(string rootPath, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default, FilterRules? rules = null)
    {
        // Sequential enumeration/stat/hash work stays off the WPF dispatcher.
        return Task.Run(async () =>
        {
            var matcher = new FilterMatcher(rules ?? new FilterRules());
            var root = Path.GetFullPath(rootPath);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The selected folder is no longer available.");
            var snapshot = new FolderSnapshot { SchemaVersion = 2, RootPath = root, Rules = matcher.Rules };
            var pending = new Stack<string>();
            pending.Push(root);
            var nextProgress = Environment.TickCount64;
            while (pending.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativeDirectory = Path.GetRelativePath(root, directory);
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    {
                        Issue(snapshot, relativeDirectory, true, "Reparse point skipped; links are not followed.");
                        continue;
                    }
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(root, entry);
                        if (matcher.Excludes(relative, false)) continue;
                        try
                        {
                            var attributes = File.GetAttributes(entry);
                            var isDirectory = (attributes & FileAttributes.Directory) != 0;
                            if (isDirectory)
                            {
                                if ((attributes & FileAttributes.ReparsePoint) != 0)
                                    Issue(snapshot, relative, true, "Reparse point skipped; links are not followed.");
                                else pending.Push(entry);
                                continue;
                            }
                            if (entry.EndsWith(SnapshotFileSuffix, StringComparison.OrdinalIgnoreCase) || !matcher.IncludesFile(relative)) continue;
                            if ((attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                Issue(snapshot, relative, false, "Reparse point skipped; links are not followed.");
                                continue;
                            }
                            if (Environment.TickCount64 >= nextProgress)
                            {
                                progress?.Report(relative);
                                nextProgress = Environment.TickCount64 + 100;
                            }
                            var info = new FileInfo(entry);
                            var size = info.Length;
                            var modified = info.LastWriteTimeUtc;
                            var hash = await _hasher.ComputeSha256Async(entry, cancellationToken);
                            info.Refresh();
                            if (!info.Exists || info.Length != size || info.LastWriteTimeUtc != modified)
                                throw new IOException("The file changed or disappeared while it was being read. Retry the scan.");
                            snapshot.Files.Add(new SnapshotFile { RelativePath = relative, Size = size, LastWriteTimeUtc = modified, Sha256 = hash });
                        }
                        catch (Exception ex) when (IsFileSystemError(ex))
                        {
                            // Unknown type: mark the prefix too, protecting descendants of a vanished directory.
                            Issue(snapshot, relative, true, Reason(ex));
                        }
                    }
                }
                catch (Exception ex) when (IsFileSystemError(ex)) { Issue(snapshot, relativeDirectory, true, Reason(ex)); }
            }
            snapshot.Files.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
            snapshot.Issues.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }, cancellationToken);
    }

    public async Task SaveAsync(FolderSnapshot snapshot, string filePath, CancellationToken cancellationToken = default)
    {
        Validate(snapshot);
        var destination = Path.GetFullPath(filePath);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<FolderSnapshot> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        FolderSnapshot snapshot;
        try { snapshot = await JsonSerializer.DeserializeAsync<FolderSnapshot>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The snapshot is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("The snapshot is not valid JSON.", ex); }
        Validate(snapshot);
        return snapshot;
    }

    private static void Validate(FolderSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is < 1 or > 2) throw new InvalidDataException("Unsupported snapshot schema version. Use a compatible FolderWatch version.");
        if (snapshot.Files is null || snapshot.Issues is null || snapshot.Rules is null)
            throw new InvalidDataException("The snapshot has missing or null collections.");
        _ = new FilterMatcher(snapshot.Rules);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in snapshot.Files)
            if (file is null || !ValidRelative(file.RelativePath) || !paths.Add(Key(file.RelativePath)) || file.Size < 0 ||
                file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("The snapshot contains an invalid file path, duplicate entry or hash.");
        foreach (var issue in snapshot.Issues)
            if (issue is null || !(issue.RelativePath == "." && issue.IsDirectory || ValidRelative(issue.RelativePath)) || string.IsNullOrWhiteSpace(issue.Reason))
                throw new InvalidDataException("The snapshot contains an invalid scan diagnostic.");
        if (snapshot.SchemaVersion == 1 && (snapshot.Rules.Include.Count > 0 || snapshot.Rules.Exclude.Count > 0 || snapshot.Issues.Count > 0))
            throw new InvalidDataException("Schema 1 snapshots cannot contain filters or scan diagnostics.");
    }

    private static bool ValidRelative(string? path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        !path.Contains(':') && !path.Any(char.IsControl) && Key(path).Split('/').All(p => p.Length > 0 && p is not "." and not "..");
    internal static string Key(string path) => path.Replace('\\', '/');
    private static bool IsFileSystemError(Exception ex) => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    private static string Reason(Exception ex) => ex is UnauthorizedAccessException or System.Security.SecurityException
        ? "Access denied. Check permissions and retry." : ex is FileNotFoundException or DirectoryNotFoundException
            ? "The file or directory disappeared during scanning. Retry the scan."
            : "Could not read the file or directory (locked, changed, or I/O error): " + ex.Message;
    private static void Issue(FolderSnapshot snapshot, string path, bool directory, string reason) =>
        snapshot.Issues.Add(new ScanIssue { RelativePath = path, IsDirectory = directory, Reason = reason });
}
