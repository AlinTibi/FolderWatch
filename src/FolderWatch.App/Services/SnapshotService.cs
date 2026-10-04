using System.IO;
using System.Text.Json;
using FolderWatch.App.Models;

namespace FolderWatch.App.Services;

public sealed class SnapshotService
{
    public const string SnapshotFileSuffix = ".folderwatch.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly HashService _hashService = new();

    public async Task<FolderSnapshot> CreateAsync(string rootPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException(rootPath);
        }

        var snapshot = new FolderSnapshot
        {
            RootPath = Path.GetFullPath(rootPath),
            CreatedUtc = DateTime.UtcNow
        };

        foreach (var filePath in EnumerateFilesSafe(snapshot.RootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                progress?.Report(filePath);
                var info = new FileInfo(filePath);
                var hash = await _hashService.ComputeSha256Async(filePath, cancellationToken);

                snapshot.Files.Add(new SnapshotFile
                {
                    RelativePath = Path.GetRelativePath(snapshot.RootPath, filePath),
                    Size = info.Length,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Sha256 = hash
                });
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        snapshot.Files.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
        return snapshot;
    }

    public async Task SaveAsync(FolderSnapshot snapshot, string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
    }

    public async Task<FolderSnapshot> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<FolderSnapshot>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException("The snapshot file is invalid.");
    }

    private static IEnumerable<string> EnumerateFilesSafe(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch (UnauthorizedAccessException)
            {
                files = Array.Empty<string>();
            }
            catch (IOException)
            {
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
                if (file.EndsWith(SnapshotFileSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch (UnauthorizedAccessException)
            {
                directories = Array.Empty<string>();
            }
            catch (IOException)
            {
                directories = Array.Empty<string>();
            }

            foreach (var directory in directories)
            {
                // Skip reparse points (symlinks/junctions): following them can revisit an
                // ancestor directory and recurse forever on a circular link.
                if (IsReparsePoint(directory))
                {
                    continue;
                }

                pending.Push(directory);
            }
        }
    }

    private static bool IsReparsePoint(string directoryPath)
    {
        try
        {
            return (File.GetAttributes(directoryPath) & FileAttributes.ReparsePoint) != 0;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
