using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Newsletters.Clients.Emails;

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

    private string emailBody;
    // private List<string> fileList;

    /// <summary>
    /// Initializes a new instance of the <see cref="HtmlBuilder"/> class.
    /// </summary>
    /// <param name="loggerInstance">The logger instance to use for logging.</param>
    /// <param name="dbInstance">The SQLite database instance to use for data access.</param>
    public HtmlBuilder(
        Logger loggerInstance,
        SQLiteDatabase dbInstance)
        : base(loggerInstance, dbInstance)
    {
        DefaultBodyAndEntry(); // set default body and entry HTML from template file if not set in config

        emailBody = Config.Body;

        newslettersDir = Config.NewsletterDir; // newsletterdir
        Directory.CreateDirectory(newslettersDir);

        // if no newsletter filename is saved or the file doesn't exist
        if (Config.NewsletterFileName.Length == 0 || File.Exists(newslettersDir + Config.NewsletterFileName))
        {
            // use date to create filename
            string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            newsletterHTMLFile = newslettersDir + currDate + "_Newsletter.html";
        }
        else
        {
            newsletterHTMLFile = newslettersDir + Config.NewsletterFileName;
        }

        Logger.Info("Newsletter will be saved to: " + newsletterHTMLFile);
    }

    /// <summary>
    /// Gets the default HTML body for the newsletter from the configuration.
    /// </summary>
    public string GetDefaultHTMLBody
    {
        get
        {
            emailBody = Config.Body;
            return emailBody;
        }
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

        Logger.Debug($"Replace Value {replaceKey} with " + replaceValue);

        // Dictionary<string, object> html_params = new Dictionary<string, object>();
        // html_params.Add("{Date}", DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        // html_params.Add(replaceKey, replaceValue);

        htmlObj = htmlObj.Replace(replaceKey, replaceValue.ToString(), StringComparison.Ordinal);
        // Logger.Debug("HERE\n " + htmlObj)

        // foreach (KeyValuePair<string, object> param in html_params)
        // {
        //     if (param.Value is not null)
        //     {
        //         htmlObj = htmlObj.Replace(param.Key, param.Value.ToString(), StringComparison.Ordinal);
        //         // Logger.Debug("HERE\n " + htmlObj)
        //     }
        // }
        
        Logger.Debug("New HTML OBJ: \n" + htmlObj);
        return htmlObj;
    }

    /// <summary>
    /// Builds chunked HTML strings from newsletter data, splitting entries into chunks based on configured email size.
    /// Groups entries by event type (Add, Update, Delete).
    /// </summary>
    /// <returns>A collection of tuples containing HTML strings and associated image streams with content IDs.</returns>
    public ReadOnlyCollection<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> Images)> BuildChunkedHtmlStringsFromNewsletterData()
    {
        // Group items by event type
        var addItems = new List<JsonFileObj>();
        var updateItems = new List<JsonFileObj>();
        var deleteItems = new List<JsonFileObj>();
        
        List<string> completed = new List<string>();

        try
        {
            Db.CreateConnection();

            foreach (var row in Db.Query("SELECT * FROM CurrNewsletterData;"))
            {
                if (row is not null)
                {
                    JsonFileObj item = JsonFileObj.ConvertToObj(row);
                    if (completed.Contains(item.Title))
                    {
                        continue;
                    }

                    // Group by event type
                    string eventType = item.EventType?.ToLowerInvariant() ?? "add";
                    switch (eventType)
                    {
                        case "add":
                            addItems.Add(item);
                            break;
                        case "update":
                            updateItems.Add(item);
                            break;
                        case "delete":
                            deleteItems.Add(item);
                            break;
                        default:
                            addItems.Add(item); // Default to add if event type is unknown
                            break;
                    }

                    completed.Add(item.Title);
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

        // Build HTML for each category
        var chunks = new List<(string, List<(MemoryStream?, string)>)>();
        StringBuilder currentChunkBuilder = new();
        var currentChunkImages = new List<(MemoryStream?, string)>();
        int currentChunkBytes = 0;
        const int overheadPerMail = 50000;
        int maxChunkSizeBytes = Config.EmailSize * 1024 * 1024; // Convert MB to bytes
        Logger.Debug($"Max email size set to {maxChunkSizeBytes} bytes");

        // Process Add items
        if (addItems.Count > 0)
        {
            string sectionHeader = GetEventSectionHeader("add");
            currentChunkBuilder.Append(sectionHeader);
            currentChunkBytes += Encoding.UTF8.GetByteCount(sectionHeader);
            
            ProcessItemsForChunks(addItems, "add", currentChunkBuilder, currentChunkImages, 
                ref currentChunkBytes, maxChunkSizeBytes, overheadPerMail, chunks);
        }

        // Process Update items
        if (updateItems.Count > 0)
        {
            string sectionHeader = GetEventSectionHeader("update");
            int headerBytes = Encoding.UTF8.GetByteCount(sectionHeader);
            
            // Check if we need a new chunk for the update section
            if (currentChunkBuilder.Length > 0 && (currentChunkBytes + headerBytes + overheadPerMail) > maxChunkSizeBytes)
            {
                Logger.Debug($"Email size exceeded before update section, finalizing current chunk. Size : {currentChunkBytes} bytes");
                chunks.Add((currentChunkBuilder.ToString(), new List<(MemoryStream?, string)>(currentChunkImages)));
                currentChunkBuilder.Clear();
                currentChunkImages.Clear();
                currentChunkBytes = 0;
            }
            
            currentChunkBuilder.Append(sectionHeader);
            currentChunkBytes += headerBytes;
            
            ProcessItemsForChunks(updateItems, "update", currentChunkBuilder, currentChunkImages, 
                ref currentChunkBytes, maxChunkSizeBytes, overheadPerMail, chunks);
        }

        // Process Delete items
        if (deleteItems.Count > 0)
        {
            string sectionHeader = GetEventSectionHeader("delete");
            int headerBytes = Encoding.UTF8.GetByteCount(sectionHeader);
            
            // Check if we need a new chunk for the delete section
            if (currentChunkBuilder.Length > 0 && (currentChunkBytes + headerBytes + overheadPerMail) > maxChunkSizeBytes)
            {
                Logger.Debug($"Email size exceeded before delete section, finalizing current chunk. Size : {currentChunkBytes} bytes");
                chunks.Add((currentChunkBuilder.ToString(), new List<(MemoryStream?, string)>(currentChunkImages)));
                currentChunkBuilder.Clear();
                currentChunkImages.Clear();
                currentChunkBytes = 0;
            }
            
            currentChunkBuilder.Append(sectionHeader);
            currentChunkBytes += headerBytes;
            
            ProcessItemsForChunks(deleteItems, "delete", currentChunkBuilder, currentChunkImages, 
                ref currentChunkBytes, maxChunkSizeBytes, overheadPerMail, chunks);
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
        StringBuilder currentChunkBuilder,
        List<(MemoryStream?, string)> currentChunkImages,
        ref int currentChunkBytes,
        int maxChunkSizeBytes,
        int overheadPerMail,
        List<(string, List<(MemoryStream?, string)>)> chunks)
    {
        foreach (var item in items)
        {
            string seaEpsHtml = string.Empty;
            if (item.Type == "Series")
            {
                var parsedInfoList = ParseSeriesInfo(item);
                seaEpsHtml += GetSeasonEpisodeHTML(parsedInfoList);
            }

            var tmp_entry = Config.Entry;

            // Track image size if needed
            int entryImageBytes = 0;
            (MemoryStream?, string) imgToAdd = default;
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
            string entryHTML = tmp_entry
                .Replace("{SeasonEpsInfo}", seaEpsHtml, StringComparison.Ordinal)
                .Replace("{ServerURL}", Config.Hostname, StringComparison.Ordinal);

            int entryBytes = Encoding.UTF8.GetByteCount(entryHTML) + entryImageBytes;

            Logger.Debug($"Processing item: {item.Title}, Event: {eventType}, Size: {entryBytes} bytes, Current Chunk Size: {currentChunkBytes} bytes");
            if (currentChunkBuilder.Length > 0 && (currentChunkBytes + entryBytes + overheadPerMail) > maxChunkSizeBytes)
            {
                // finalize current chunk as one part (HTML fragment)
                Logger.Debug($"Email size exceeded, finalizing current chunk. Size : {currentChunkBytes} bytes");
                chunks.Add((currentChunkBuilder.ToString(), new List<(MemoryStream?, string)>(currentChunkImages)));
                currentChunkBuilder.Clear();
                currentChunkImages.Clear();
                currentChunkBytes = 0;
                
                // Add section header again in new chunk if we're continuing this category
                string sectionHeader = GetEventSectionHeader(eventType);
                currentChunkBuilder.Append(sectionHeader);
                currentChunkBytes += Encoding.UTF8.GetByteCount(sectionHeader);
            }

            currentChunkBuilder.Append(entryHTML);
            currentChunkImages.Add(imgToAdd);
            currentChunkBytes += entryBytes;
        }
    }

    /// <summary>
    /// Gets the HTML section header for an event type.
    /// </summary>
    private string GetEventSectionHeader(string eventType)
    {
        var (title, emoji, color) = eventType.ToLowerInvariant() switch
        {
            "add" => ("Added to Library", "🎬", "#4CAF50"),
            "update" => ("Updated in Library", "🔄", "#2196F3"),
            "delete" => ("Removed from Library", "🗑️", "#F44336"),
            _ => ("Added to Library", "🎬", "#4CAF50")
        };

        return $@"
        <tr>
            <td colspan='2' style='padding: 20px 10px 10px 10px;'>
                <h2 style='color: {color}; margin: 0; font-size: 1.8em; border-bottom: 2px solid {color}; padding-bottom: 10px;'>
                    {emoji} {title}
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
    public string BuildHtmlStringsForTest()
    {
        StringBuilder testHTML = new StringBuilder();

        try
        {
            // Create test entries for each event type
            string[] eventTypes = { "add", "update", "delete" };
            string[] titles = { "Test Series - Added", "Test Movie - Updated", "Test Series - Removed" };

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

                string tmp_entry = Config.Entry;

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
                string entryHTML = tmp_entry
                    .Replace("{SeasonEpsInfo}", seaEpsHtml, StringComparison.Ordinal)
                    .Replace("{ServerURL}", Config.Hostname, StringComparison.Ordinal);

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
        string html = string.Empty;
        foreach (NlDetailsJson obj in list)
        {
            Logger.Debug("SNIPPET OBJ: " + JsonConvert.SerializeObject(obj));
            // html += "<div id='SeasonEpisode' class='text' style='color: #FFFFFF;'>Season: " + obj.Season + " - Eps. " + obj.EpisodeRange + "</div>";
            html += "Season: " + obj.Season + " - Eps. " + obj.EpisodeRange + "<br>";
        }

        return html;
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

    private void DefaultBodyAndEntry()
    {
        Logger.Debug("Checking for default Body and Entry HTML from Template file..");
        try
        {
            var pluginDir = Path.GetDirectoryName(typeof(HtmlBuilder).Assembly.Location);
            if (pluginDir == null)
            {
                Logger.Error("Failed to locate plugin directory.");
            }
            
            if (string.IsNullOrWhiteSpace(Config.Body)) 
            {
                try
                {
                    Config.Body = File.ReadAllText($"{pluginDir}/Templates/template_modern_body.html");
                    Logger.Debug("Body HTML set from Template file!");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to set default Body HTML from Template file");
                    Logger.Error(ex);
                }
            }

            if (string.IsNullOrWhiteSpace(Config.Entry))
            {
                try
                {
                    Config.Entry = File.ReadAllText($"{pluginDir}/Templates/template_modern_entry.html");
                    Logger.Debug("Entry HTML set from Template file!");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to set default Entry HTML from Template file");
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
