using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using PostmarkDotNet;
using Quartz;
using VsWindowsLayoutMonitor.Settings;

namespace VsWindowsLayoutMonitor.Jobs;

/// <summary>
///     Quartz job that monitors Visual Studio's ApplicationPrivateSettings.xml for changes in
///     window layout definitions, writes time-stamped snapshots, logs current layouts,
///     and optionally sends Postmark email notifications when layouts are removed.
/// </summary>
/// <remarks>
///     - The target collection inside the XML is identified by the <see cref="TargetCollectionName" />.
///     - Snapshots are stored under the "WindowsLayouts" folder relative to <see cref="AppContext.BaseDirectory" />.
///     - The job is decorated with <see cref="DisallowConcurrentExecutionAttribute" /> to prevent concurrent runs.
/// </remarks>
[DisallowConcurrentExecution]
public class MonitorJob : IJob
{
    #region Static

    /// <summary>
    ///     The fully-qualified name of the Visual Studio settings collection that stores window layouts.
    /// </summary>
    private const string TargetCollectionName = "Microsoft.VisualStudio.Platform.WindowManagement.Layouts.WindowLayoutInfoList";

    /// <summary>
    ///     Lower-cased version of <see cref="TargetCollectionName" /> for case-insensitive comparisons.
    /// </summary>
    private static readonly string TargetCollectionNameNormalized = TargetCollectionName.ToLowerInvariant();

    /// <summary>
    ///     Indicates whether the current layouts have been logged once at startup.
    ///     Used to avoid logging the full list on every run unless a change is detected.
    /// </summary>
    private static bool hasLoggedInitialLayouts;

    #endregion

    #region Private Fields

    /// <summary>
    ///     Logger instance for diagnostics and operational information.
    /// </summary>
    private readonly ILogger<MonitorJob> logger;

    /// <summary>
    ///     Provides access to the current <see cref="ApplicationSettings" /> including Postmark settings and XML path.
    /// </summary>
    private readonly IOptionsMonitor<ApplicationSettings> appSettings;

    #endregion

    #region Constructors

    /// <summary>
    ///     Initializes a new instance of the <see cref="MonitorJob" /> class.
    /// </summary>
    /// <param name="logger">The logger used to emit diagnostic messages.</param>
    /// <param name="appSettings">The application settings monitor providing configuration values.</param>
    public MonitorJob(ILogger<MonitorJob> logger, IOptionsMonitor<ApplicationSettings> appSettings)
    {
        this.logger = logger;
        this.appSettings = appSettings;
    }

    #endregion

    #region IJob Members

    /// <summary>
    ///     Executes the monitoring workflow:
    ///     resolves the settings XML path, reads and parses the target collection,
    ///     compares against the latest snapshot, writes a new snapshot on changes,
    ///     logs the current layouts, and sends Postmark notifications for removed layouts.
    /// </summary>
    /// <param name="context">The Quartz job execution context (includes the cancellation token).</param>
    /// <returns>A task that completes when execution finishes.</returns>
    /// <remarks>
    ///     If the configured XML path is not supplied, a default VS 2022 settings path is used.
    ///     The method reads the XML with shared read access to tolerate Visual Studio locking the file.
    /// </remarks>
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var cancellationToken = context.CancellationToken;

            // Resolve the Visual Studio ApplicationPrivateSettings.xml path from configuration
            var configuredPath = appSettings.CurrentValue.XmlSettingsFilePath;

            var vsSettingsPath = !string.IsNullOrWhiteSpace(configuredPath) ? Environment.ExpandEnvironmentVariables(configuredPath) : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "VisualStudio",
                "17.0_459a930b",
                "ApplicationPrivateSettings.xml"
            );

            if(!File.Exists(vsSettingsPath))
            {
                logger.LogWarning("ApplicationPrivateSettings.xml not found at path: {path}", vsSettingsPath);

                return;
            }

            // Load the XML (allow shared read as VS may have the file open)
            XDocument sourceDoc;

            await using(var fs = new FileStream(
                            vsSettingsPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite
                        ))
            {
                sourceDoc = await XDocument.LoadAsync(
                    fs,
                    LoadOptions.PreserveWhitespace,
                    cancellationToken
                );
            }

            // Find the target <collection ...> node (case-insensitive match on the name attribute)
            var collectionElement = sourceDoc.Descendants("collection")
                .FirstOrDefault(e => string.Equals(
                        (string?) e.Attribute("name"),
                        TargetCollectionName,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if(collectionElement is null)
            {
                logger.LogWarning("Target collection '{name}' not found in ApplicationPrivateSettings.xml", TargetCollectionName);

                return;
            }

            // Prepare destination folder
            var snapshotsFolder = Path.Combine(AppContext.BaseDirectory, "WindowsLayouts");
            Directory.CreateDirectory(snapshotsFolder);

            // Extract current layout keys and names
            var currentLayouts = ExtractLayouts(collectionElement);

            // Compare to previous snapshot
            var latestSnapshotPath = GetLatestSnapshotPath(snapshotsFolder);
            var shouldSave = false;
            List<string> removedNamesForEmail = new();

            if(!string.IsNullOrWhiteSpace(latestSnapshotPath) && File.Exists(latestSnapshotPath))
            {
                try
                {
                    XDocument prevDoc;

                    await using(var prevFs = new FileStream(
                                    latestSnapshotPath,
                                    FileMode.Open,
                                    FileAccess.Read,
                                    FileShare.Read
                                ))
                    {
                        prevDoc = await XDocument.LoadAsync(
                            prevFs,
                            LoadOptions.PreserveWhitespace,
                            cancellationToken
                        );
                    }

                    var prevCollection = prevDoc.Root?.Name.LocalName == "collection" ? prevDoc.Root as XElement : prevDoc.Descendants("collection")
                        .FirstOrDefault();

                    var prevLayouts = prevCollection is not null ? ExtractLayouts(prevCollection) : new Dictionary<string, string>();

                    var addedKeys = currentLayouts.Keys.Except(prevLayouts.Keys)
                        .ToList();

                    var removedKeys = prevLayouts.Keys.Except(currentLayouts.Keys)
                        .ToList();

                    if(addedKeys.Count > 0 || removedKeys.Count > 0)
                    {
                        shouldSave = true;

                        if(addedKeys.Count > 0)
                        {
                            var addedNames = addedKeys.Select(k => currentLayouts.TryGetValue(k, out var n) ? n : k);
                            logger.LogInformation("Layouts added: {names}", string.Join(", ", addedNames));
                        }

                        if(removedKeys.Count > 0)
                        {
                            var removedNames = removedKeys.Select(k => prevLayouts.TryGetValue(k, out var n) ? n : k)
                                .ToList();

                            removedNamesForEmail = removedNames;
                            logger.LogInformation("Layouts removed: {names}", string.Join(", ", removedNames));
                        }
                    }
                }
                catch(Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to compare with previous snapshot: {path}",
                        latestSnapshotPath
                    );

                    shouldSave = false;
                }
            }
            else
            {
                // No previous snapshot found; create the initial baseline snapshot.
                logger.LogInformation("No previous snapshot found; saving initial baseline snapshot.");
                shouldSave = true;
            }

            if(shouldSave)
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                var outPath = Path.Combine(snapshotsFolder, $"{timestamp}.xml");

                var outDoc = new XDocument(
                    new XDeclaration(
                        "1.0",
                        "utf-8",
                        "yes"
                    ),
                    new XComment($" Saved at {DateTimeOffset.UtcNow:o} "),
                    new XElement(collectionElement) // clone as root
                );

                await using var outFs = new FileStream(
                    outPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                );

                await outDoc.SaveAsync(
                    outFs,
                    SaveOptions.None,
                    cancellationToken
                );

                logger.LogInformation("Saved Windows layout snapshot: {file}", outPath);
            }

            // Log current layouts only once at app start, and thereafter only when changed
            var shouldLogCurrentLayouts = !hasLoggedInitialLayouts || shouldSave;

            if(shouldLogCurrentLayouts)
            {
                if(currentLayouts.Count > 0)
                {
                    var names = string.Join(", ", currentLayouts.Values.OrderBy(v => v));
                    logger.LogInformation("Current layouts: {names}", names);
                }
                else
                {
                    logger.LogInformation("Current layouts: none found");
                }

                hasLoggedInitialLayouts = true;
            }

            // Send Postmark notification if any were removed
            if(removedNamesForEmail.Count > 0)
            {
                await SendPostmarkNotificationAsync(removedNamesForEmail);
            }
        }
        catch(OperationCanceledException)
        {
            logger.LogInformation("MonitorJob cancelled at: {time}", DateTimeOffset.Now);
        }
        catch(Exception ex)
        {
            logger.LogError(ex, "Error executing MonitorJob");
        }

        await Task.CompletedTask;
    }

    #endregion

    #region Private Methods

    /// <summary>
    ///     Gets the full path of the most recent snapshot file in the specified folder.
    /// </summary>
    /// <param name="folder">The folder containing snapshot XML files named as yyyyMMddHHmmss.xml.</param>
    /// <returns>
    ///     The full path to the latest snapshot file, or <c>null</c> if the folder does not exist
    ///     or no matching files are found.
    /// </returns>
    private static string? GetLatestSnapshotPath(string folder)
    {
        if(!Directory.Exists(folder))
        {
            return null;
        }

        // Files are named as yyyyMMddHHmmss.xml, so sort descending by filename
        var latest = Directory.EnumerateFiles(
                folder,
                "*.xml",
                SearchOption.TopDirectoryOnly
            )
            .OrderByDescending(Path.GetFileNameWithoutExtension)
            .FirstOrDefault();

        return latest;
    }

    /// <summary>
    ///     Extracts a mapping of layout keys (GUIDs) to human-readable display names from the target collection element.
    /// </summary>
    /// <param name="collectionElement">The collection element from ApplicationPrivateSettings.xml containing layout data.</param>
    /// <returns>
    ///     A dictionary where the key is the layout GUID (string) and the value is the display name.
    ///     If no valid JSON payload is found, an empty dictionary is returned.
    /// </returns>
    /// <remarks>
    ///     The method parses a JSON array from the collection's "value" element.
    ///     Each item is expected to contain a "Key" and a "Value" string; the display name is
    ///     derived from the third pipe-delimited segment of "Value" when available.
    /// </remarks>
    private static Dictionary<string, string> ExtractLayouts(XElement collectionElement)
    {
        // Returns Key (GUID) -> Display Name mapping
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var valueElement = collectionElement.Elements("value")
            .FirstOrDefault(v => string.Equals(
                    (string?) v.Attribute("name"),
                    "value",
                    StringComparison.OrdinalIgnoreCase
                )
            );

        var json = valueElement?.Value;

        if(string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);

            foreach(var item in doc.RootElement.EnumerateArray())
            {
                if(!item.TryGetProperty("Key", out var keyProp))
                {
                    continue;
                }

                var key = keyProp.GetString();

                if(string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var displayName = key;

                if(item.TryGetProperty("Value", out var valueProp))
                {
                    var valueStr = valueProp.GetString();

                    if(!string.IsNullOrEmpty(valueStr))
                    {
                        var parts = valueStr.Split('|');

                        if(parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
                        {
                            displayName = parts[2];
                        }
                    }
                }

                result[key] = displayName;
            }
        }
        catch
        {
            // Ignore malformed JSON; return what we gathered so far
        }

        return result;
    }

    /// <summary>
    ///     Sends an email notification via Postmark listing the names of layouts that were removed.
    /// </summary>
    /// <param name="removedNames">The collection of removed layout display names.</param>
    /// <returns>A task that completes when all email send attempts have finished.</returns>
    /// <remarks>
    ///     If Postmark settings are missing or incomplete, the method logs a warning and does not attempt to send emails.
    ///     Each recipient is processed individually; failures are logged per-recipient.
    /// </remarks>
    private async Task SendPostmarkNotificationAsync(List<string> removedNames)
    {
        var pm = appSettings.CurrentValue.Postmark;

        if(pm is null || string.IsNullOrWhiteSpace(pm.ServerToken) || string.IsNullOrWhiteSpace(pm.SenderAddress) || pm.Recipients is null || pm.Recipients.Count == 0)
        {
            logger.LogWarning("Postmark settings are missing or incomplete; skipping email notification.");

            return;
        }

        var client = new PostmarkClient(pm.ServerToken);
        var subject = "Visual Studio layouts removed";

        var bodyBuilder = new StringBuilder();
        bodyBuilder.AppendLine("The following Visual Studio window layouts were removed:");

        foreach(var name in removedNames)
        {
            bodyBuilder.AppendLine($"- {name}");
        }

        foreach(var recipient in pm.Recipients)
        {
            if(string.IsNullOrWhiteSpace(recipient.Email))
            {
                continue;
            }

            var message = new PostmarkMessage
            {
                From = pm.SenderAddress!,
                To = recipient.Email!,
                Subject = subject,
                TextBody = bodyBuilder.ToString()
            };

            try
            {
                var response = await client.SendMessageAsync(message);

                if(response.Status != PostmarkStatus.Success)
                {
                    logger.LogWarning(
                        "Postmark send failed to {email}: {error}",
                        recipient.Email,
                        response.Message
                    );
                }
                else
                {
                    logger.LogInformation("Postmark notification sent to {email}", recipient.Email);
                }
            }
            catch(Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error sending Postmark notification to {email}",
                    recipient.Email
                );
            }
        }
    }

    /// <summary>
    ///     Serializes the provided <see cref="XElement" /> to a compact, canonical string without
    ///     indentation or newlines, suitable for string-based comparisons.
    /// </summary>
    /// <param name="element">The XML element to serialize.</param>
    /// <returns>A canonical string representation of the XML element.</returns>
    private static string Canonicalize(XElement element)
    {
        // Serialize with no indentation/newlines to achieve a canonical comparable string
        using var sw = new StringWriter(CultureInfo.InvariantCulture);

        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.None
        };

        using(var xw = XmlWriter.Create(sw, settings))
        {
            element.Save(xw);
        }

        return sw.ToString();
    }

    #endregion
}