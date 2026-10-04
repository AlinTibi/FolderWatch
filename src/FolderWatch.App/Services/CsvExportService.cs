using System.IO;
using System.Text;
using FolderWatch.App.Models;

namespace FolderWatch.App.Services;

public sealed class CsvExportService
{
    private static readonly string[] Header = { "Status", "File", "Old Size", "New Size" };

    public async Task ExportAsync(IEnumerable<ComparisonItem> items, string filePath, CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', Header));

        foreach (var item in items)
        {
            builder.AppendLine(string.Join(',',
                Escape(item.Change.ToString()),
                Escape(item.RelativePath),
                Escape(FormatSize(item.OldSize)),
                Escape(FormatSize(item.NewSize))));
        }

        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(filePath, builder.ToString(), utf8WithBom, cancellationToken);
    }

    private static string FormatSize(long? size) => size?.ToString() ?? string.Empty;

    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
