using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FolderWatch.App.Models;
using FolderWatch.App.Services;
using Microsoft.Win32;

namespace FolderWatch.App;

public partial class MainWindow : Window
{
    private readonly SnapshotService _snapshotService = new();
    private readonly CompareService _compareService = new();
    private readonly CsvExportService _csvExportService = new();

    private IReadOnlyList<ComparisonItem> _allResults = Array.Empty<ComparisonItem>();
    private CancellationTokenSource? _operation;
    private TimeSpan _comparisonDuration;

    public MainWindow()
    {
        InitializeComponent();
        FilterCombo.SelectedIndex = 0;
        SetBusy(false);
        Closing += (_, args) =>
        {
            if (_operation is not null) { _operation.Cancel(); args.Cancel = true; StatusText.Text = "Cancelling; close again when the operation finishes."; }
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder"
        };

        if (dialog.ShowDialog() == true)
        {
            FolderPathBox.Text = dialog.FolderName;
            StatusText.Text = "Folder selected.";
            ResetResults();
        }
    }

    private async void CreateSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureFolderSelected())
        {
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Save FolderWatch snapshot",
            Filter = "FolderWatch snapshot (*.folderwatch.json)|*.folderwatch.json|JSON files (*.json)|*.json",
            FileName = $"FolderWatch-{DateTime.Now:yyyyMMdd-HHmmss}.folderwatch.json"
        };

        if (saveDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var rules = GetRules();
            _operation = new CancellationTokenSource();
            SetBusy(true, "Scanning folder...");
            var timer = Stopwatch.StartNew();
            var progress = new Progress<string>(path => StatusText.Text = $"Scanning: {path}");
            var snapshot = await _snapshotService.CreateAsync(FolderPathBox.Text, progress, _operation.Token, rules);
            await _snapshotService.SaveAsync(snapshot, saveDialog.FileName, _operation.Token);
            StatusText.Text = $"{(snapshot.IsPartial ? "Partial snapshot" : "Snapshot")} saved — Files captured: {snapshot.Files.Count:N0}; Skipped/inaccessible entries: {snapshot.Issues.Count:N0}; Duration: {timer.Elapsed.TotalSeconds:F2}s.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Snapshot cancelled. No snapshot was saved."; }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            _operation?.Dispose(); _operation = null;
            SetBusy(false);
        }
    }

    private async void CompareSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureFolderSelected())
        {
            return;
        }

        var openDialog = new OpenFileDialog
        {
            Title = "Open FolderWatch snapshot",
            Filter = "FolderWatch snapshot (*.folderwatch.json)|*.folderwatch.json|JSON files (*.json)|*.json"
        };

        if (openDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _operation = new CancellationTokenSource();
            SetBusy(true, "Loading snapshot...");
            var previous = await _snapshotService.LoadAsync(openDialog.FileName, _operation.Token);
            var rulesChanged = !CurrentRulesMatch(previous.Rules);
            SetRules(previous.Rules);
            RulesNotice.Text = rulesChanged ? "Using snapshot rules; current rules were replaced to keep this comparison consistent." : "Using snapshot rules.";

            var timer = Stopwatch.StartNew();
            var progress = new Progress<string>(path => StatusText.Text = $"Scanning: {path}");
            var current = await _snapshotService.CreateAsync(FolderPathBox.Text, progress, _operation.Token, previous.Rules);
            var results = await Task.Run(() => _compareService.Compare(previous, current), _operation.Token);
            _operation.Token.ThrowIfCancellationRequested();
            _allResults = results;
            _comparisonDuration = timer.Elapsed;
            ApplyFilter();

            var stats = CompareService.ComputeStats(_allResults);
            var summary = $"Compared {stats.Total:N0} entries in {_comparisonDuration.TotalSeconds:F2}s — Added: {stats.Added}, Removed: {stats.Removed}, Modified: {stats.Modified}, Unchanged: {stats.Unchanged}, Inaccessible: {stats.Inaccessible}.";

            if (previous.Files.Count > 0 && current.Files.Count > 0 && stats.Modified == 0 && stats.Unchanged == 0)
            {
                summary += " Warning: no matching files were found — the snapshot may be from a different folder.";
            }

            StatusText.Text = summary;
        }
        catch (OperationCanceledException) { StatusText.Text = "Comparison cancelled. Previous results retained."; }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            _operation?.Dispose(); _operation = null;
            SetBusy(false);
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_allResults.Count == 0)
        {
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Export comparison results",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"FolderWatch-Results-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (saveDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SetBusy(true, "Exporting CSV...");
            var visibleResults = CompareService.ApplyFilter(_allResults, GetSelectedFilter());
            await _csvExportService.ExportAsync(visibleResults, saveDialog.FileName);
            StatusText.Text = $"Exported {visibleResults.Count:N0} row(s) to {Path.GetFileName(saveDialog.FileName)}.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ResetResults();
        StatusText.Text = "Results cleared.";
    }

    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ResetResults()
    {
        _allResults = Array.Empty<ComparisonItem>();
        _comparisonDuration = TimeSpan.Zero;
        FilterCombo.SelectedIndex = 0;
        ApplyFilter();
        SetBusy(false);
    }

    private void ApplyFilter()
    {
        var filtered = CompareService.ApplyFilter(_allResults, GetSelectedFilter());
        ResultsGrid.ItemsSource = filtered;
        UpdateStatistics();
    }

    private void UpdateStatistics()
    {
        var stats = CompareService.ComputeStats(_allResults);
        TotalCountText.Text = stats.Total.ToString();
        AddedCountText.Text = stats.Added.ToString();
        RemovedCountText.Text = stats.Removed.ToString();
        ModifiedCountText.Text = stats.Modified.ToString();
        UnchangedCountText.Text = stats.Unchanged.ToString();
        InaccessibleCountText.Text = stats.Inaccessible.ToString();
        DurationText.Text = $"{_comparisonDuration.TotalSeconds:F2}s";
    }

    private ResultFilter GetSelectedFilter()
    {
        var tag = (FilterCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        return tag is not null && Enum.TryParse<ResultFilter>(tag, out var filter) ? filter : ResultFilter.All;
    }

    private bool EnsureFolderSelected()
    {
        if (!string.IsNullOrWhiteSpace(FolderPathBox.Text) && Directory.Exists(FolderPathBox.Text))
        {
            return true;
        }

        MessageBox.Show(this, "Select a valid folder first.", "FolderWatch", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        BrowseButton.IsEnabled = !busy;
        CreateSnapshotButton.IsEnabled = !busy;
        CompareSnapshotButton.IsEnabled = !busy;
        FilterCombo.IsEnabled = !busy;
        RulesPanel.IsEnabled = !busy;
        CancelButton.Visibility = busy && _operation is not null ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = true;

        var hasResults = _allResults.Count > 0;
        ExportCsvButton.IsEnabled = !busy && hasResults;
        ClearButton.IsEnabled = !busy && hasResults;

        if (status is not null)
        {
            StatusText.Text = status;
        }
    }

    private void ShowError(Exception ex)
    {
        StatusText.Text = "Operation failed.";
        MessageBox.Show(this, ex.Message, "FolderWatch", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private FilterRules GetRules() => FilterMatcher.Normalize(new FilterRules
    {
        Include = IncludeBox.Text.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList(),
        Exclude = ExcludeBox.Text.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList()
    });
    private bool CurrentRulesMatch(FilterRules rules)
    {
        try { return FilterMatcher.Equivalent(GetRules(), rules); }
        catch (InvalidDataException) { return false; }
    }
    private void SetRules(FilterRules rules)
    {
        IncludeBox.Text = string.Join("; ", rules.Include);
        ExcludeBox.Text = string.Join("; ", rules.Exclude);
    }
    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        var preset = (sender as Button)?.Tag as string;
        SetRules(new FilterRules { Exclude = preset switch
        {
            "Development" => new() { ".git", ".vs", "bin", "obj", "node_modules" },
            "Temporary" => new() { "*.tmp", "*.log", "Thumbs.db" },
            _ => new()
        } });
        RulesNotice.Text = "Rules apply to the next snapshot. Comparisons always use the saved snapshot rules.";
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _operation?.Cancel(); CancelButton.IsEnabled = false; StatusText.Text = "Cancelling...";
    }
}
