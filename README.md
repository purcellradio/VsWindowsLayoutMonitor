# VsWindowsLayoutMonitor

A small Windows background app that watches your Visual Studio window layouts and keeps timestamped backups when they change.

What it does
- Reads Visual Studio's ApplicationPrivateSettings.xml.
- Extracts the WindowLayoutInfoList collection and parses the layout list.
- Detects when layouts are added or removed (by layout key/name).
- Saves a snapshot of just that collection to the WindowsLayouts folder using a timestamped filename (yyyyMMddHHmmss.xml).
- Logs what changed. Sends an email (Postmark) when layouts are removed.
- Logs the current set of layouts once at app start and whenever layouts change.

Configuration
1) Application settings (appsettings.json or User Secrets)
- ApplicationSettings.XmlSettingsFilePath
  - Full path to Visual Studio's ApplicationPrivateSettings.xml.
  - Supports environment variables, e.g. %LOCALAPPDATA%/Microsoft/VisualStudio/17.0_459a930b/ApplicationPrivateSettings.xml.
  - If the service runs under a different account, point this to that user’s file.
- ApplicationSettings.Postmark
  - ServerToken: your Postmark server token.
  - SenderAddress: from-address the email will use.
  - Recipients: list of Name/Email objects to notify when layouts are removed.

Example (User Secrets recommended for secrets)
{
  "ApplicationSettings": {
    "XmlSettingsFilePath": "%LOCALAPPDATA%/Microsoft/VisualStudio/17.0_459a930b/ApplicationPrivateSettings.xml",
    "Postmark": {
      "ServerToken": "<redacted>",
      "SenderAddress": "noreply@example.com",
      "Recipients": [ { "Name": "Ops", "Email": "ops@example.com" } ]
    }
  }
}

Where files are written
- Snapshots: WindowsLayouts folder under the app’s working directory.
  - Example: <app base>\WindowsLayouts\20250815142201.xml
- Logs: Logs/VsWindowsLayoutMonitor-<date>.log under the app’s working directory (daily rolling).

How it runs
- It’s a .NET Worker Service. When started, it schedules a periodic check (via Quartz).
- On first run, it creates a baseline snapshot and logs the current layouts.
- Afterwards, it only saves and logs when layouts are added or removed.

Sample log (redacted)
15/08/2025 16:28:46.906 [INF] MonitorHostedService scheduled MonitorJob.
15/08/2025 16:28:47.024 [INF] Current layouts: Eddie Green Dual Monitor Layout
15/08/2025 16:29:16.953 [INF] Layouts added: New Layout 2, New Layout 1
15/08/2025 16:29:16.965 [INF] Saved Windows layout snapshot: C:\...\WindowsLayouts\20250815152916.xml
15/08/2025 16:29:16.967 [INF] Current layouts: Eddie Green Dual Monitor Layout, New Layout 1, New Layout 2
15/08/2025 16:30:16.948 [INF] Layouts removed: New Layout 2, New Layout 1
15/08/2025 16:30:16.950 [INF] Saved Windows layout snapshot: C:\...\WindowsLayouts\20250815153016.xml
15/08/2025 16:30:16.951 [INF] Current layouts: Eddie Green Dual Monitor Layout
15/08/2025 16:30:17.686 [INF] Postmark notification sent to ***@***.com
15/08/2025 16:31:29.479 [INF] MonitorHostedService task was canceled.

Notes
- Only layout key presence/absence is tracked (adds/removals). Content or ordering changes that do not add/remove keys are ignored.
- If you don’t want email notifications, leave Postmark settings unset.