using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller.Library;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Newsletters.Clients.Email;

/// <summary>
/// Builds HTML content for newsletters, including templating and chunking logic.
/// </summary>
public class HtmlBuilder : ClientBuilder
{
    // Global Vars
    // Constant fields
    private const string Append = "Append";
    private const string Write = "Overwrite";

    // Readonly
    private readonly string newslettersDir;
    private readonly string newsletterHTMLFile;

    // private readonly string[] itemJsonKeys = 

    private string emailBody = string.Empty;
    private string emailEntry = string.Empty;
    // private List<string> fileList;

    /// <summary>
    /// Initializes a new instance of the <see cref="HtmlBuilder"/> class.
    /// </summary>
    /// <param name="loggerInstance">The logger instance to use for logging.</param>
    /// <param name="dbInstance">The SQLite database instance to use for data access.</param>
    /// <param name="emailConfig">The email configuration to use for templates.</param>
    /// <param name="libraryManager">The library manager for resolving library names.</param>
    public HtmlBuilder(
        Logger loggerInstance,
        SQLiteDatabase dbInstance,
        EmailConfiguration emailConfig,
        ILibraryManager libraryManager)
        : base(loggerInstance, dbInstance, libraryManager)
    {
        DefaultBodyAndEntry(emailConfig); // set default body and entry HTML from template file if not set in config

        newslettersDir = Config.NewsletterDir; // newsletterdir
        Directory.CreateDirectory(newslettersDir);

        // Always generate a unique filename
        string currDate = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
        string configName = emailConfig.Name;
        // Sanitize config name for filename
        configName = string.Join("_", configName.Split(Path.GetInvalidFileNameChars()));
        newsletterHTMLFile = Path.Combine(newslettersDir, $"{currDate}_{configName}_Newsletter.html");

        Logger.Info("Newsletter will be saved to: " + newsletterHTMLFile);
    }

    /// <summary>
    /// Gets the default HTML body for the newsletter from the configuration.
    /// </summary>
    /// <param name="emailConfig">The email configuration to use for templates.</param>
    /// <returns>The default HTML body string.</returns>
    public string GetDefaultHTMLBody(EmailConfiguration emailConfig)
    {
        return emailBody;
    }

    /// <summary>
    /// Replaces a specified key in the HTML template with the provided value.
    /// </summary>
    /// <param name="htmlObj">The HTML template string.</param>
    /// <param name="replaceKey">The key to be replaced in the template.</param>
    /// <param name="replaceValue">The value to replace the key with.</param>
    /// <returns>The HTML string with the key replaced by the value.</returns>
    public string TemplateReplace(string htmlObj, string replaceKey, object replaceValue)
    {
        Logger.Debug("Replacing {} params:\n " + htmlObj);
        if (replaceValue is null)
        {
            Logger.Debug($"Replace string is null.. Nothing to replace");
            return htmlObj;
        }

        if (replaceKey == "{RunTime}" && (int)replaceValue == 0)
        {
            Logger.Debug($"{replaceKey} == {replaceValue}");
            Logger.Debug("Skipping replace..");
            return htmlObj;
        }

        if (replaceKey == "{CommunityRating}" && replaceValue is float rating)
        {
            replaceValue = rating.ToString($"F{Config.CommunityRatingDecimalPlaces}", System.Globalization.CultureInfo.InvariantCulture);
        }

        Logger.Debug($"Replace Value {replaceKey} with " + replaceValue);

        htmlObj = htmlObj.Replace(replaceKey, replaceValue.ToString(), StringComparison.Ordinal);

        Logger.Debug("New HTML OBJ: \n" + htmlObj);
        return htmlObj;
    }

    /// <summary>
    /// Builds chunked HTML strings from newsletter data, splitting entries into chunks based on configured email size.
    /// Groups entries by event type (Add, Update, Delete), then by library name (Movies first, then Series).
    /// </summary>
    /// <param name="serverId">The Jellyfin server ID to include in item URLs.</param>
    /// <param name="emailConfig">The email configuration to use for templates.</param>
    /// <returns>A collection of tuples containing HTML strings and associated image streams with content IDs.</returns>
    public ReadOnlyCollection<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> Images)> BuildChunkedHtmlStringsFromNewsletterData(string serverId, EmailConfiguration emailConfig)
    {
        // Build library name map for resolving LibraryId -> LibraryName
        var libraryNameMap = BuildLibraryNameMap();

        var itemsByKey = new Dictionary<string, JsonFileObj>(); // Key: "Title_EventType", deduplicates and collects

        try
        {
            Db.CreateConnection();

            foreach (var row in Db.Query("SELECT * FROM CurrNewsletterData;"))
            {
                if (row is not null)
                {
                    JsonFileObj item = JsonFileObj.ConvertToObj(row);
                    string eventType = item.EventType?.ToLowerInvariant() ?? "add";
                    
                    // Check if the event type should be included based on configuration
                    if (!ShouldIncludeItem(item, emailConfig, "Email"))
                    {
                        continue;
                    }

                    // Create a unique key combining title and event type
                    string uniqueKey = $"{item.Title}_{eventType}";
                    if (itemsByKey.ContainsKey(uniqueKey))
                    {
                        continue;
                    }

                    itemsByKey[uniqueKey] = item;
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

        // Sort items: event type (add -> update -> delete), then Movie libraries first, then by library name
        var eventTypeOrder = new Dictionary<string, int> { { "add", 0 }, { "update", 1 }, { "delete", 2 } };
        var sortedItems = itemsByKey.Values
            .OrderBy(i => eventTypeOrder.GetValueOrDefault(i.EventType?.ToLowerInvariant() ?? "add", 0))
            .ThenBy(i => i.Type == "Movie" ? 0 : 1)
            .ThenBy(i => GetLibraryName(i.LibraryId, libraryNameMap))
            .ToList();

        // Group sorted items by event type, then by library name (order is preserved from sort)
        var groupedItems = sortedItems
            .GroupBy(i => i.EventType?.ToLowerInvariant() ?? "add")
            .Select(eventGroup => new
            {
                EventType = eventGroup.Key,
                Libraries = eventGroup
                    .GroupBy(i => GetLibraryName(i.LibraryId, libraryNameMap))
                    .Select(libGroup => new { LibraryName = libGroup.Key, Items = libGroup.ToList() })
            });

        // Build HTML for each category
        var chunks = new List<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> Images)>();
        StringBuilder currentChunkBuilder = new();
        var currentChunkImages = new List<(MemoryStream? ImageStream, string ContentId)>();
        int currentChunkBytes = 0;
        const int overheadPerMail = 50000;
        int maxChunkSizeBytes = Config.EmailSize * 1024 * 1024; // Convert MB to bytes
        Logger.Debug($"Max email size set to {maxChunkSizeBytes} bytes");

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
                    emailConfig);
            }
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
        List<JsonFileObj> items,
        string eventType,
        string libraryName,
        StringBuilder currentChunkBuilder,
        List<(MemoryStream? ImageStream, string ContentId)> currentChunkImages,
        ref int currentChunkBytes,
        int maxChunkSizeBytes,
        int overheadPerMail,
        List<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> Images)> chunks,
        string serverId,
        EmailConfiguration emailConfig)
    {
        foreach (var item in items)
        {
            string seaEpsHtml = string.Empty;
            if (item.Type == "Series")
            {
                var parsedInfoList = ParseSeriesInfo(item);
                seaEpsHtml += GetSeasonEpisodeHTML(parsedInfoList);
            }

            var tmp_entry = emailEntry;

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

            foreach (var ele in item.GetReplaceDict())
            {
                if (ele.Value is not null)
                {
                    tmp_entry = this.TemplateReplace(tmp_entry, ele.Key, ele.Value);
                }
            }

            // Add event badge to the entry
            string eventBadge = GetEventBadge(eventType);
            tmp_entry = tmp_entry.Replace("{EventBadge}", eventBadge, StringComparison.Ordinal);

            // Compose the entry's HTML now (for accurate size)
            string itemUrl = string.IsNullOrEmpty(Config.Hostname) 
                ? string.Empty
                : $"{Config.Hostname}/web/index.html#/details?id={item.ItemID}&serverId={serverId}&event={eventType}";
            string entryHTML = tmp_entry
                .Replace("{SeasonEpsInfo}", seaEpsHtml, StringComparison.Ordinal)
                .Replace("{ItemURL}", itemUrl, StringComparison.Ordinal);

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
    /// Gets the HTML section header for an event type and library name.
    /// </summary>
    private string GetEventSectionHeader(string eventType, string libraryName = "Library")
    {
        var (title, emoji, color) = eventType.ToLowerInvariant() switch
        {
            "add" => ($"Added to {libraryName}", "🎬", "#4CAF50"),
            "update" => ($"Updated in {libraryName}", "🔄", "#2196F3"),
            "delete" => ($"Removed from {libraryName}", "🗑️", "#F44336"),
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

    /// <summary>
    /// Gets the HTML badge for an event type to be displayed on individual entries.
    /// </summary>
    private string GetEventBadge(string eventType)
    {
        var (label, emoji, bgColor) = eventType.ToLowerInvariant() switch
        {
            "add" => ("NEW", "🎬", "#4CAF50"),
            "update" => ("UPDATED", "🔄", "#2196F3"),
            "delete" => ("REMOVED", "🗑️", "#F44336"),
            _ => ("NEW", "🎬", "#4CAF50")
        };

        return $@"<span style='display: inline-block; background-color: {bgColor}; color: white; padding: 4px 8px; border-radius: 4px; font-size: 0.75em; font-weight: bold; margin-left: 8px;'>{emoji} {label}</span>";
    }

    /// <summary>
    /// Builds a sample HTML string for testing newsletter entry rendering.
    /// Shows all three event types (Add, Update, Delete).
    /// </summary>
    /// <returns>A string containing the HTML for test newsletter entries with all event types.</returns>
    /// <param name="emailConfig">The email configuration to use for templates.</param>
    public string BuildHtmlStringsForTest(EmailConfiguration emailConfig)
    {
        StringBuilder testHTML = new StringBuilder();

        try
        {
            // Create test entries for each event type
            string[] eventTypes = { "add", "update", "delete" };
            string[] titles = { "Test Series", "Test Movie", "Test Series" };

            foreach (var eventType in eventTypes)
            {
                // Add section header
                testHTML.Append(GetEventSectionHeader(eventType));

                JsonFileObj item = JsonFileObj.GetTestObj();
                
                // Customize the title based on event type
                int eventIndex = Array.IndexOf(eventTypes, eventType);
                item.Title = titles[eventIndex];
                
                Logger.Debug($"Test Entry ITEM ({eventType}): " + JsonConvert.SerializeObject(item));

                string seaEpsHtml = "Season: 1 - Eps. 1 - 10<br>Season: 2 - Eps. 1 - 10<br>Season: 3 - Eps. 1 - 10";

                string tmp_entry = emailEntry;

                foreach (KeyValuePair<string, object?> ele in item.GetReplaceDict())
                {
                    if (ele.Value is not null)
                    {
                        tmp_entry = this.TemplateReplace(tmp_entry, ele.Key, ele.Value);
                    }
                }

                // Add event badge for current event type
                string eventBadge = GetEventBadge(eventType);
                tmp_entry = tmp_entry.Replace("{EventBadge}", eventBadge, StringComparison.Ordinal);

                // Compose the entry's HTML now
                string itemUrl = string.IsNullOrEmpty(Config.Hostname) 
                    ? string.Empty
                    : Config.Hostname;
                string entryHTML = tmp_entry
                    .Replace("{SeasonEpsInfo}", seaEpsHtml, StringComparison.Ordinal)
                    .Replace("{ItemURL}", itemUrl, StringComparison.Ordinal);

                testHTML.Append(entryHTML);
            }
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }

        return testHTML.ToString();
    }

    /// <summary>
    /// Replaces the {EntryData} placeholder in the newsletter body with the provided newsletter data string.
    /// </summary>
    /// <param name="body">The HTML body template containing the {EntryData} placeholder.</param>
    /// <param name="nlData">The newsletter data to insert into the body.</param>
    /// <returns>The HTML body with the {EntryData} placeholder replaced by the newsletter data.</returns>
    public static string ReplaceBodyWithBuiltString(string body, string nlData)
    {
        return body.Replace("{EntryData}", nlData, StringComparison.Ordinal);
    }

    private string GetSeasonEpisodeHTML(IReadOnlyCollection<NlDetailsJson> list)
    {
        string baseText = GetSeasonEpisodeBase(list);
        // Convert newlines to HTML <br> tags and trim the trailing newline
        return baseText.TrimEnd('\r', '\n').Replace("\n", "<br>", StringComparison.Ordinal);
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

    private void DefaultBodyAndEntry(EmailConfiguration emailConfig)
    {
        Logger.Debug("Checking for default Body and Entry HTML from Template file..");
        
        // Initialize fields based on config
        this.emailBody = emailConfig.Body ?? string.Empty;
        this.emailEntry = emailConfig.Entry ?? string.Empty;

        try
        {
            var pluginDir = Path.GetDirectoryName(typeof(HtmlBuilder).Assembly.Location);
            if (pluginDir == null)
            {
                Logger.Error("Failed to locate plugin directory.");
                return;
            }
            
            // Determine category
            string category = !string.IsNullOrEmpty(emailConfig.TemplateCategory) ? emailConfig.TemplateCategory : "Modern";

            if (string.IsNullOrWhiteSpace(this.emailBody))
            {
                try
                {
                    string bodyTemplate = File.ReadAllText($"{pluginDir}/Templates/{category}/template_body.html");
                    this.emailBody = bodyTemplate;
                    Logger.Debug($"Body HTML set from Template file ({category}) internally!");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to set default Body HTML from Template file");
                    Logger.Error(ex);
                }
            }

            if (string.IsNullOrWhiteSpace(this.emailEntry))
            {
                try
                {
                    string entryTemplate = File.ReadAllText($"{pluginDir}/Templates/{category}/template_entry.html");
                    this.emailEntry = entryTemplate;
                    Logger.Debug($"Entry HTML set from Template file ({category}) internally!");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to set default Entry HTML from Template file");
                    Logger.Error(ex);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error("Failed to locate/set html body from template file..");
            Logger.Error(e);
        }
    }
}
