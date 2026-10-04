using System.IO;
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

    public MainWindow()
    {
        InitializeComponent();
        FilterCombo.SelectedIndex = 0;
        SetBusy(false);
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
            SetBusy(true, "Scanning folder...");
            var progress = new Progress<string>(path => StatusText.Text = $"Scanning: {path}");
            var snapshot = await _snapshotService.CreateAsync(FolderPathBox.Text, progress);
            await _snapshotService.SaveAsync(snapshot, saveDialog.FileName);
            StatusText.Text = $"Snapshot saved. {snapshot.Files.Count:N0} files scanned.";
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
            SetBusy(true, "Loading snapshot...");
            var previous = await _snapshotService.LoadAsync(openDialog.FileName);

            var progress = new Progress<string>(path => StatusText.Text = $"Scanning: {path}");
            var current = await _snapshotService.CreateAsync(FolderPathBox.Text, progress);
            _allResults = _compareService.Compare(previous, current);
            ApplyFilter();

            var stats = CompareService.ComputeStats(_allResults);
            StatusText.Text = $"Comparison complete — Added: {stats.Added}, Removed: {stats.Removed}, Modified: {stats.Modified}, Unchanged: {stats.Unchanged}.";
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
}
