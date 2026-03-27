using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Integrations;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller.Library;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Newsletters.Clients.Email;

/// <summary>
/// Builds HTML content for email newsletters, including chunking logic for large emails.
/// </summary>
/// <param name="loggerInstance">The logger instance for logging operations.</param>
/// <param name="dbInstance">The database instance for data access.</param>
/// <param name="emailConfig">The email configuration to use for templates.</param>
/// <param name="libraryManager">The library manager for resolving library names.</param>
/// <param name="upcomingItems">The list of prefetched upcoming media items.</param>
public class HtmlBuilder(
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    EmailConfiguration emailConfig,
    ILibraryManager libraryManager,
    IReadOnlyList<JsonFileObj> upcomingItems)
    : HtmlContentBuilder(loggerInstance, dbInstance, libraryManager, upcomingItems)
{
    // Constant fields
    private const string Append = "Append";
    private const string Write = "Overwrite";

    // Readonly fields initialized from the Plugin Config directly
    private readonly string newslettersDir = Plugin.Instance!.Configuration.NewsletterDir;
    private readonly string newsletterHTMLFile = GetNewsletterHTMLFileLocation(emailConfig.Name);

    /// <inheritdoc/>
    protected override string DefaultTemplateCategory => "Modern";

    private static string GetNewsletterHTMLFileLocation(string configName)
    {
        string currDate = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
        string safeName = string.Join("_", configName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(Plugin.Instance!.Configuration.NewsletterDir, $"{currDate}_{safeName}_Newsletter.html");
    }

    /// <summary>
    /// Builds chunked HTML strings from newsletter data, splitting entries into chunks based on configured email size.
    /// Groups entries by event type (Add, Update, Delete), then by library name (Movies first, then Series).
    /// </summary>
    /// <param name="serverId">The Jellyfin server ID to include in item URLs.</param>
    /// <param name="config">The email configuration to use for templates.</param>
    /// <returns>A collection of tuples containing HTML strings and associated image streams with content IDs.</returns>
    public ReadOnlyCollection<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> Images)> BuildChunkedHtmlStringsFromNewsletterData(string serverId, EmailConfiguration config)
    {
        EnsureSetup(config);
        Directory.CreateDirectory(newslettersDir);
        Logger.Info("Newsletter will be saved to: " + newsletterHTMLFile);

        var groupedItems = BuildGroupedItems(config, "Email");

        // Build HTML for each category
        var chunks = new List<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> Images)>();
        StringBuilder currentChunkBuilder = new();
        var currentChunkImages = new List<(MemoryStream? ImageStream, string ContentId)>();
        int currentChunkBytes = 0;
        const int overheadPerMail = 50000;
        int maxChunkSizeBytes = Config.EmailSize * 1024 * 1024; // Convert MB to bytes
        Logger.Debug($"Max email size set to {maxChunkSizeBytes} bytes");

        try
        {
            // Open connection for ParseSeriesInfo calls inside BuildEntryHtml
            Db.CreateConnection();

            foreach (var eventGroup in groupedItems)
            {
                foreach (var library in eventGroup.Libraries)
                {
                    if (library.Items.Count == 0)
                    {
                        continue;
                    }

                    string sectionHeader = GetEventSectionHeader(eventGroup.EventType, library.LibraryName);
                    int headerBytes = Encoding.UTF8.GetByteCount(sectionHeader);

                    // Check if we need a new chunk for this section
                    if (currentChunkBuilder.Length > 0 && (currentChunkBytes + headerBytes + overheadPerMail) > maxChunkSizeBytes)
                    {
                        Logger.Debug($"Email size exceeded before {eventGroup.EventType}/{library.LibraryName} section, finalizing current chunk. Size : {currentChunkBytes} bytes");
                        chunks.Add((currentChunkBuilder.ToString(), new List<(MemoryStream? ImageStream, string ContentId)>(currentChunkImages)));
                        currentChunkBuilder.Clear();
                        currentChunkImages.Clear();
                        currentChunkBytes = 0;
                    }

                    currentChunkBuilder.Append(sectionHeader);
                    currentChunkBytes += headerBytes;

                    ProcessItemsForChunks(
                        library.Items,
                        eventGroup.EventType,
                        library.LibraryName,
                        currentChunkBuilder,
                        currentChunkImages,
                        ref currentChunkBytes,
                        maxChunkSizeBytes,
                        overheadPerMail,
                        chunks,
                        serverId,
                        config);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }
        finally
        {
            Db.CloseConnection();
        }

        // Add final chunk if any
        if (currentChunkBuilder.Length > 0)
        {
            Logger.Debug($"Adding final chunk. Size : {currentChunkBytes} bytes");
            chunks.Add((currentChunkBuilder.ToString(), currentChunkImages));
        }

        return chunks.AsReadOnly();
    }

    /// <summary>
    /// Processes a list of items and adds them to chunks, managing size limits.
    /// </summary>
    private void ProcessItemsForChunks(
        IReadOnlyList<JsonFileObj> items,
        string eventType,
        string libraryName,
        StringBuilder currentChunkBuilder,
        List<(MemoryStream? ImageStream, string ContentId)> currentChunkImages,
        ref int currentChunkBytes,
        int maxChunkSizeBytes,
        int overheadPerMail,
        List<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> Images)> chunks,
        string serverId,
        EmailConfiguration config)
    {
        foreach (var item in items)
        {
            // Track image size if needed
            int entryImageBytes = 0;
            (MemoryStream? ImageStream, string ContentId) imgToAdd = default;
            if (Config.PosterType == "attachment")
            {
                var (resizedStream, contentId, success) = ResizeImage(item.PosterPath);

                if (success)
                {
                    item.ImageURL = $"cid:{contentId}";
                    entryImageBytes = (resizedStream != null) ? (int)Math.Ceiling(resizedStream.Length * 4.0 / 3.0) : 0; // Base64 encoding overhead;
                    imgToAdd = (resizedStream, contentId);
                }
            }

            string entryHTML = BuildEntryHtml(item, eventType, serverId);
            int entryBytes = Encoding.UTF8.GetByteCount(entryHTML) + entryImageBytes;

            Logger.Debug($"Processing item: {item.Title}, Event: {eventType}, Library: {libraryName}, Size: {entryBytes} bytes, Current Chunk Size: {currentChunkBytes} bytes");
            if (currentChunkBuilder.Length > 0 && (currentChunkBytes + entryBytes + overheadPerMail) > maxChunkSizeBytes)
            {
                // finalize current chunk as one part (HTML fragment)
                Logger.Debug($"Email size exceeded, finalizing current chunk. Size : {currentChunkBytes} bytes");
                chunks.Add((currentChunkBuilder.ToString(), new List<(MemoryStream? ImageStream, string ContentId)>(currentChunkImages)));
                currentChunkBuilder.Clear();
                currentChunkImages.Clear();
                currentChunkBytes = 0;

                // Add section header again in new chunk if we're continuing this category
                string sectionHeader = GetEventSectionHeader(eventType, libraryName);
                currentChunkBuilder.Append(sectionHeader);
                currentChunkBytes += Encoding.UTF8.GetByteCount(sectionHeader);
            }

            currentChunkBuilder.Append(entryHTML);
            currentChunkImages.Add(imgToAdd);
            currentChunkBytes += entryBytes;
        }
    }

    /// <summary>
    /// Builds a sample HTML string for testing newsletter entry rendering.
    /// Shows all event types (Add, Update, Delete, Upcoming).
    /// </summary>
    /// <param name="config">The email configuration to use for templates.</param>
    /// <returns>A string containing the HTML for test newsletter entries with all event types.</returns>
    public string BuildHtmlStringsForTest(EmailConfiguration config)
    {
        return BuildTestEntriesHtml(config);
    }

    /// <summary>
    /// Saves the provided HTML body to the newsletter file.
    /// </summary>
    /// <param name="htmlBody">The HTML content to save to the file.</param>
    public void CleanUp(string htmlBody)
    {
        // save newsletter to file
        Logger.Info("Saving HTML file");
        WriteFile(Write, newsletterHTMLFile, htmlBody);
    }

    /// <inheritdoc/>
    protected override string GetEventSectionHeader(string eventType, string libraryName = "Library")
    {
        var (title, emoji, color) = eventType.ToLowerInvariant() switch
        {
            "add" => ($"Added to {libraryName}", "🎬", "#4CAF50"),
            "update" => ($"Updated in {libraryName}", "🔄", "#2196F3"),
            "delete" => ($"Removed from {libraryName}", "🗑️", "#F44336"),
            "upcoming" => ($"Upcoming in {libraryName}", "📅", "#FF8C00"),
            _ => ($"Added to {libraryName}", "🎬", "#4CAF50")
        };

        return $@"
        <tr>
            <td colspan='2' style='padding: 20px 10px 10px 10px;'>
                <h2 style='color: {color}; margin: 0; font-size: 1.8em; border-bottom: 2px solid {color}; padding-bottom: 10px; display: flex; align-items: center; gap: 8px;'>
                   <span style='margin-right: 4px;'>{emoji}</span> {title}
                </h2>
            </td>
        </tr>";
    }

    /// <inheritdoc/>
    protected override string GetEventBadge(string eventType)
    {
        var (label, emoji, bgColor) = eventType.ToLowerInvariant() switch
        {
            "add" => ("NEW", "🎬", "#4CAF50"),
            "update" => ("UPDATED", "🔄", "#2196F3"),
            "delete" => ("REMOVED", "🗑️", "#F44336"),
            "upcoming" => ("UPCOMING", "📅", "#FF8C00"),
            _ => ("NEW", "🎬", "#4CAF50")
        };

        return $@"<span style='display: inline-block; background-color: {bgColor}; color: white; padding: 4px 8px; border-radius: 4px; font-size: 0.75em; font-weight: bold; margin-left: 8px;'>{emoji} {label}</span>";
    }

    private static void WriteFile(string method, string path, string value)
    {
        if (method == Append)
        {
            File.AppendAllText(path, value);
        }
        else if (method == Write)
        {
            File.WriteAllText(path, value);
        }
    }
}
