namespace VsWindowsLayoutMonitor.Settings;

/// <summary>
/// Configuration settings for sending emails via Postmark.
/// </summary>
public class PostmarkSettings
{
    /// <summary>
    /// Postmark server token used for API authentication.
    /// </summary>
    public string? ServerToken { get; set; }

    /// <summary>
    /// Default sender email address (From).
    /// </summary>
    public string? SenderAddress { get; set; }

    /// <summary>
    /// Recipients that should receive notifications.
    /// </summary>
    public List<MailAddressSettings>? Recipients { get; set; }
}