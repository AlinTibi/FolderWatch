# Contributing to FolderWatch

Use an issue to describe a reproducible bug or a concrete workflow improvement. Include the application and Windows versions, reproduction steps, expected/actual results, and relevant error text. Discuss large changes before implementing them.

Never attach credentials, private files, personal paths or unredacted screenshots. Report vulnerabilities privately to security@almarfeld.com; general support is support@almarfeld.com.

## Build and test

Use the source requirements in README.md. Run from the repository root:

```powershell
dotnet restore FolderWatch.sln
dotnet build FolderWatch.sln --configuration Release --no-restore
dotnet test FolderWatch.sln --configuration Release --no-build
```

## Pull requests

Keep each PR focused, explain the problem and resulting behavior, and report the checks actually run. Add regression coverage for behavior changes and real screenshots for UI changes. Preserve offline operation, privacy guarantees and product-specific limitations. Signed commits are preferred. Do not include secrets or unrelated generated build files. Collaborate respectfully and discuss technical tradeoffs directly.
