using System.Globalization;
using System.Text;
using FolderWatch.App.Models;

namespace FolderWatch.App.Services;

public sealed class CsvExportService
{
    public async Task ExportAsync(IEnumerable<ComparisonItem> items, string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(filePath);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None), new UTF8Encoding(true)))
            {
                await writer.WriteLineAsync("Status,Relative Path,Old Size,New Size,Old Modified,New Modified,Reason");
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var cells = new[] { item.Change.ToString(), item.RelativePath, item.OldSize?.ToString(CultureInfo.InvariantCulture) ?? "",
                        item.NewSize?.ToString(CultureInfo.InvariantCulture) ?? "", item.OldModified?.ToString("O") ?? "",
                        item.NewModified?.ToString("O") ?? "", item.Reason };
                    await writer.WriteLineAsync(string.Join(',', cells.Select(Escape)));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Escape(string value) => value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
        ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
}
