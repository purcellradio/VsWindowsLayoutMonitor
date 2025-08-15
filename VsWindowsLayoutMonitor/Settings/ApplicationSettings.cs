namespace VsWindowsLayoutMonitor.Settings;

/// <summary>
/// Represents application-wide configuration settings.
/// </summary>
public class ApplicationSettings
{
    /// <summary>
    /// Optional: Full path to the Visual Studio ApplicationPrivateSettings.xml to read.
    /// Supports environment variable expansion (e.g., %LOCALAPPDATA%).
    /// </summary>
    public string? XmlSettingsFilePath { get; set; }

    /// <summary>
    /// Optional Postmark email delivery settings.
    /// </summary>
    public PostmarkSettings? Postmark { get; set; }
}