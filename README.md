# FolderWatch

FolderWatch is a lightweight, fully offline Windows desktop tool for snapshotting a folder and later detecting what changed in it — files added, removed or modified — using SHA-256 hashing.

## Features

- Select any local folder and create a JSON snapshot of its contents
- Snapshot stores relative path, size, modified time and SHA-256 hash per file
- Compare a saved snapshot against the current folder state
- Detect Added, Removed, Modified and Unchanged files
- Filter results by status (All, Added, Removed, Modified, Unchanged)
- Live statistics above the results grid
- Clear results while keeping the selected folder
- Export the filtered results to CSV
- Dark desktop UI, fully offline, no external NuGet dependencies

## Requirements

- Windows 10 or Windows 11 (x64)
- No separate .NET install needed to run the published release — it's self-contained

## Download

Grab the latest portable build from the [Releases page](https://github.com/AlinTibi/FolderWatch/releases/latest):

- `FolderWatch-vX.X.X-win-x64.zip` — unzip and run `FolderWatch.exe`. No installer, no .NET runtime required.

## Usage

1. Select a folder
2. Create a snapshot
3. Later, select the same folder again
4. Compare it against the saved snapshot
5. Filter the results and export them to CSV if needed

## Build from source

Requirements: Windows, .NET 8 SDK.

```powershell
dotnet restore FolderWatch.sln
dotnet build FolderWatch.sln -c Release
```

## Privacy

FolderWatch runs fully offline. It sends no telemetry, phones home to nothing, and uploads no data — snapshots and comparisons stay on your machine.

## License

MIT License. See [LICENSE](LICENSE).
