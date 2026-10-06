using System.IO;
using System.Security.Cryptography;

namespace FolderWatch.App.Services;

public interface IFileHasher
{
    Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default);
}

public sealed class HashService : IFileHasher
{
    public async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            // Writers and deletes cannot race a successful capture on Windows.
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
