# FolderWatch v1.1.0 — release candidate (unreleased)

- Read failures, locks, files changing during capture and skipped reparse points are recorded as Inaccessible with a reason. A partial directory scan never makes its unknown contents look deleted.
- Partial snapshots retain successfully captured files and show captured/skipped entry counts. An incomplete baseline also prevents unsupported Added claims.
- Optional include/exclude glob rules and Development/Temporary presets. The default remains no exclusions. Names match at any depth; relative paths match from the root; `*`, `?` and `**` are supported.
- Schema v2 saves rules and diagnostics. Schema v1 snapshots from v1.0.0 remain readable and comparable with their original unfiltered scope.
- Comparison automatically uses the saved snapshot rules, explains scope changes and includes all five status counts, total entries and duration.
- CSV follows the current status filter and includes relative path, sizes, UTC modified timestamps and diagnostic reasons.
- Sequential streaming hashes and throttled progress run off the UI thread. Cancel scan preserves existing results and never saves a cancelled snapshot. Links are not followed.
- Dedicated automated tests and mandatory test/build/portable-publish CI checks.
- Self-contained Windows x64 ZIP, consistent 1.1.0 executable metadata and SHA-256 sidecar. No separate .NET runtime or WebView2 is needed.

Rules are session-only; no new settings file is written. Saved snapshots keep their rules. None / Reset clears the rules for new snapshots.

Limits: inaccessible counts describe diagnostic entries, including directories; they cannot count unknown files inside an unreadable directory. Capture is not a filesystem-wide atomic transaction. Per-file sharing restrictions and before/after checks reject detected changes; retry partial scans when files are available.
