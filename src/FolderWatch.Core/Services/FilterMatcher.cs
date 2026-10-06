using System.Text.RegularExpressions;
using FolderWatch.App.Models;

namespace FolderWatch.App.Services;

public sealed class FilterMatcher
{
    private readonly Regex[] _include;
    private readonly Regex[] _exclude;
    public FilterRules Rules { get; }

    public FilterMatcher(FilterRules rules)
    {
        Rules = Normalize(rules);
        _include = Rules.Include.Select(Compile).ToArray();
        _exclude = Rules.Exclude.Select(Compile).ToArray();
    }

    public bool IncludesFile(string path) => !Excludes(path, false) &&
        (_include.Length == 0 || _include.Any(r => r.IsMatch(Canonical(path))));

    // Includes never prune directories: descendants may still match an include.
    public bool Excludes(string path, bool directory)
    {
        path = Canonical(path);
        if (_exclude.Any(r => r.IsMatch(path))) return true;
        var slash = path.LastIndexOf('/');
        while (slash > 0)
        {
            if (_exclude.Any(r => r.IsMatch(path[..slash]))) return true;
            slash = path.LastIndexOf('/', slash - 1);
        }
        return false;
    }

    public static FilterRules Normalize(FilterRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return new FilterRules { Include = Clean(rules.Include), Exclude = Clean(rules.Exclude) };
    }

    public static bool Equivalent(FilterRules a, FilterRules b)
    {
        a = Normalize(a); b = Normalize(b);
        return a.Include.SequenceEqual(b.Include, StringComparer.OrdinalIgnoreCase) &&
            a.Exclude.SequenceEqual(b.Exclude, StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> Clean(List<string> patterns)
    {
        if (patterns is null || patterns.Count > 100) throw new InvalidDataException("Use at most 100 patterns per field.");
        var result = patterns.Select(p => Canonical(p?.Trim() ?? string.Empty).TrimEnd('/'))
            .Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var p in result)
            if (p.Length > 512 || p.StartsWith('/') || p.Contains(':') ||
                p.Split('/').Any(s => s is ".." or ".") || p.Any(char.IsControl))
                throw new InvalidDataException("Patterns must be relative paths, names or wildcards (*, ?, **). No '..' or absolute paths.");
        return result;
    }

    private static string Canonical(string path) => path.Replace('\\', '/');
    private static Regex Compile(string pattern)
    {
        // A directory/** exclusion also matches the directory itself for pruning.
        var relativePathPattern = pattern.Contains('/');
        var descendants = pattern.EndsWith("/**", StringComparison.Ordinal);
        if (descendants) pattern = pattern[..^3];
        var expression = new System.Text.StringBuilder(relativePathPattern ? "^" : "(?:^|/)");
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                i++;
                if (i + 1 < pattern.Length && pattern[i + 1] == '/') { i++; expression.Append("(?:.*/)?"); }
                else expression.Append(".*");
            }
            else expression.Append(pattern[i] switch { '*' => "[^/]*", '?' => "[^/]", _ => Regex.Escape(pattern[i].ToString()) });
        }
        expression.Append(descendants ? "(?:/.*)?$" : "$");
        return new Regex(expression.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }
}
