# FolderWatch

FolderWatch is a lightweight Windows desktop utility for creating snapshots of folders and detecting file changes over time.

## Current features

- Select any local folder
- Create a JSON snapshot of all accessible files
- Store relative path, size, modified time and SHA-256 hash
- Compare a saved snapshot with the current folder state
- Detect Added, Removed, Modified and Unchanged files
- Dark Windows desktop UI
- Fully offline
- No external NuGet dependencies

## Tech

- C#
- .NET 8
- WPF
- System.Text.Json
- SHA-256 hashing

## Build

Requirements:

- Windows 10 or Windows 11
- .NET 8 SDK

```powershell
dotnet restore FolderWatch.sln
dotnet build FolderWatch.sln -c Release
```

## Status

Early development version. The core snapshot and comparison workflow is implemented.

Planned next steps include filtering, CSV export, cancellation/progress improvements, better result visualization and packaged releases.

## License

MIT License.
