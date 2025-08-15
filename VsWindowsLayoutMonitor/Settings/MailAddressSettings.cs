namespace VsWindowsLayoutMonitor.Settings;

/// <summary>
/// Represents a mail address entry with an optional display name and email address.
/// </summary>
/// <remarks>
/// Used for sender and recipient addresses in outbound email configuration.
/// </remarks>
public class MailAddressSettings
{
    /// <summary>
    /// Optional display name to associate with the email address.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Email address (e.g., user@domain.tld).
    /// </summary>
    public string? Email { get; set; }
}