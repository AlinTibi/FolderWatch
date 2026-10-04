using System.Windows;
using FolderWatch.App.Services;
using Microsoft.Win32;

namespace FolderWatch.App;

public partial class MainWindow : Window
{
    private readonly SnapshotService _snapshotService = new();
    private readonly CompareService _compareService = new();

    public MainWindow()
    {
        InitializeComponent();
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
            ResultsGrid.ItemsSource = null;
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
            var results = _compareService.Compare(previous, current);

            ResultsGrid.ItemsSource = results;

            var added = results.Count(x => x.Change == Models.ChangeType.Added);
            var removed = results.Count(x => x.Change == Models.ChangeType.Removed);
            var modified = results.Count(x => x.Change == Models.ChangeType.Modified);
            StatusText.Text = $"Comparison complete — Added: {added}, Removed: {removed}, Modified: {modified}.";
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
