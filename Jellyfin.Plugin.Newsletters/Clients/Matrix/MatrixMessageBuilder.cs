using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Integrations;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller.Library;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Newsletters.Clients.Matrix;

/// <summary>
/// Builds HTML and plaintext content for Matrix newsletters based on templates.
/// </summary>
public class MatrixMessageBuilder(
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManager,
    IReadOnlyList<JsonFileObj> upcomingItems)
    : ClientBuilder(loggerInstance, dbInstance, libraryManager)
{
    private static readonly HttpClient _httpClient = new();

    private string matrixBodyHtml = string.Empty;
    private string matrixEntryHtml = string.Empty;
    private bool _isSetup;

    private void EnsureSetup(MatrixConfiguration config)
    {
        if (_isSetup)
        {
            return;
        }

        DefaultBodyAndEntry(config);
        _isSetup = true;
    }

    /// <summary>
    /// Gets the default HTML body wrapper for the newsletter.
    /// </summary>
    /// <param name="config">The Matrix configuration.</param>
    /// <returns>The Default HTML builder string.</returns>
    public string GetDefaultHTMLBody(MatrixConfiguration config)
    {
        EnsureSetup(config);
        return matrixBodyHtml;
    }

    /// <summary>
    /// Replaces templated values in an HTML string.
    /// </summary>
    /// <param name="htmlObj">The original HTML string.</param>
    /// <param name="replaceKey">The key to replace.</param>
    /// <param name="replaceValue">The value to replace the key with.</param>
    /// <returns>The updated HTML string.</returns>
    public string TemplateReplace(string htmlObj, string replaceKey, object replaceValue)
    {
        if (replaceValue is null)
        {
            Logger.Debug($"Replace string is null.. Defaulting to N/A");
            replaceValue = "N/A";
        }

        if (replaceKey == "{RunTime}" && (int)replaceValue == 0)
        {
            Logger.Debug($"{replaceKey} == {replaceValue}");
            Logger.Debug("Defaulting to N/A");
            replaceValue = "N/A";
        }

        if (replaceKey == "{CommunityRating}" && replaceValue is float rating)
        {
            replaceValue = rating > 0 ? rating.ToString($"F{Config.CommunityRatingDecimalPlaces}", CultureInfo.InvariantCulture) : "N/A";
        }

        Logger.Debug($"Replace Value {replaceKey} with " + replaceValue);
        
        htmlObj = htmlObj.Replace(replaceKey, replaceValue.ToString(), StringComparison.Ordinal);

        Logger.Debug("New HTML OBJ: \n" + htmlObj);
        return htmlObj;
    }

    /// <summary>
    /// Builds the rich HTML fallback from the current newsletter data.
    /// </summary>
    /// <param name="serverId">The ID of the server.</param>
    /// <param name="config">The Matrix configuration.</param>
    /// <returns>A string containing HTML body.</returns>
    public string BuildMessageFromNewsletterData(string serverId, MatrixConfiguration config)
    {
        EnsureSetup(config);

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
                    if (!ShouldIncludeItem(item, config, "Matrix"))
                    {
                        continue;
                    }

                    // Create a unique key combining title and event type
                    string uniqueKey = $"{item.Title}_{eventType}";
                    if (!itemsByKey.ContainsKey(uniqueKey))
                    {
                        itemsByKey[uniqueKey] = item;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error("An error occurred: " + e);
        }
        finally
        {
            Db.CloseConnection();
        }

        // Append prefetched upcoming items and deduplicate by title
        if (upcomingItems != null && upcomingItems.Count > 0)
        {
            foreach (var item in upcomingItems)
            {
                string eventType = item.EventType?.ToLowerInvariant() ?? "add";
                string uniqueKey = $"{item.Title}_{eventType}";
                if (itemsByKey.ContainsKey(uniqueKey))
                {
                    continue;
                }

                itemsByKey[uniqueKey] = item;
            }
        }

        var allItems = itemsByKey.Values.ToList();

        // Sort items: event type (add -> update -> delete -> upcoming), then Movie libraries first, then by library name
        var eventTypeOrder = new Dictionary<string, int> { { "add", 0 }, { "update", 1 }, { "delete", 2 }, { "upcoming", 3 } };

        var sortedItems = allItems
            .OrderBy(i => eventTypeOrder.GetValueOrDefault(i.EventType?.ToLowerInvariant() ?? "add", 0))
            .ThenBy(i => i.Type == "Movie" ? 0 : 1)
            .ThenBy(i => i.EventType == "upcoming" ? i.LibraryId : GetLibraryName(i.LibraryId, libraryNameMap))
            .ToList();

        // Group sorted items by event type, then by library name (order is preserved from sort)
        var groupedItems = sortedItems
            .GroupBy(i => i.EventType?.ToLowerInvariant() ?? "add")
            .Select(eventGroup => new
            {
                EventType = eventGroup.Key,
                Libraries = eventGroup
                    .GroupBy(i => i.EventType == "upcoming" ? i.LibraryId : GetLibraryName(i.LibraryId, libraryNameMap))
                    .Select(libGroup => new { LibraryName = libGroup.Key, Items = libGroup.ToList() })
            });

        StringBuilder contentBuilderHtml = new();

        foreach (var eventGroup in groupedItems)
        {
            foreach (var library in eventGroup.Libraries)
            {
                if (library.Items.Count == 0)
                {
                    continue;
                }

                string sectionHtml = GetEventSectionHeader(eventGroup.EventType, library.LibraryName);
                contentBuilderHtml.Append(sectionHtml);

                ProcessItems(library.Items, eventGroup.EventType, contentBuilderHtml, serverId, config);
            }
        }

        return ReplaceBodyWithBuiltString(GetDefaultHTMLBody(config), contentBuilderHtml.ToString());
    }

    private void ProcessItems(
        List<JsonFileObj> items,
        string eventType,
        StringBuilder htmlBuilder,
        string serverId,
        MatrixConfiguration config)
    {
        foreach (var item in items)
        {
            string seaEpsHtml = string.Empty;
            if (item.Type == "Series")
            {
                var parsedInfoList = ParseSeriesInfo(item, upcomingItems);
                string seaEpsPlain = GetSeasonEpisodeBase(parsedInfoList);
                seaEpsHtml = seaEpsPlain.TrimEnd('\r', '\n').Replace("\n", "<br>", StringComparison.Ordinal);
            }

            var tmpEntryHtml = matrixEntryHtml;

            // Upload the image to the Matrix homeserver and use the MXC URL for the image
            // Fallback to HTTP ImageURL if it is an upcoming item, or if upload fails/is unavailable
            var replaceDict = item.GetReplaceDict();
            
            string? mxcUrl = null;
            if (Config.PosterType == "attachment" && eventType != "upcoming")
            {
                mxcUrl = UploadImageToMatrix(item.PosterPath, false, config);
            }
            else if (!string.IsNullOrEmpty(item.ImageURL))
            {
                mxcUrl = UploadImageToMatrix(item.ImageURL, true, config);
            }

            if (!string.IsNullOrEmpty(mxcUrl))
            {
                replaceDict["{ImageURL}"] = mxcUrl;
            }

            foreach (var ele in replaceDict)
            {
                if (ele.Value is not null)
                {
                    tmpEntryHtml = this.TemplateReplace(tmpEntryHtml, ele.Key, ele.Value);
                }
            }

            string eventBadge = GetEventBadge(eventType);
            tmpEntryHtml = tmpEntryHtml.Replace("{EventBadge}", eventBadge, StringComparison.Ordinal);

            string itemUrl = string.IsNullOrEmpty(Config.Hostname) || eventType == "upcoming" 
                ? string.Empty
                : $"{Config.Hostname}/web/index.html#/details?id={item.ItemID}&serverId={serverId}&event={eventType}";
            
            string entryHTML = tmpEntryHtml
                .Replace("{SeasonEpsInfo}", seaEpsHtml, StringComparison.Ordinal)
                .Replace("{ItemURL}", itemUrl, StringComparison.Ordinal);

            htmlBuilder.Append(entryHTML);
        }
    }

    /// <summary>
    /// Builds a sample message for testing.
    /// </summary>
    /// <param name="config">The Matrix configuration.</param>
    /// <returns>A string containing HTML body.</returns>
    public string BuildMessageForTest(MatrixConfiguration config)
    {
        EnsureSetup(config);

        StringBuilder testHTML = new StringBuilder();

        string[] eventTypes = { "add", "update", "delete", "upcoming" };
        string[] titles = { "Test Added Series", "Test Updated Movie", "Test Deleted Series", "Test Upcoming Movie" };

        for (int i = 0; i < eventTypes.Length; i++)
        {
            string eventType = eventTypes[i];
            
            testHTML.Append(GetEventSectionHeader(eventType));

            JsonFileObj item = JsonFileObj.GetTestObj();
            item.Title = titles[i];

            string seaEpsHtml = "Season: 1 - Eps. 1 - 10<br>Season: 2 - Eps. 1 - 10<br>Season: 3 - Eps. 1 - 10";

            string tmpEntryHtml = matrixEntryHtml;
            var replaceDict = item.GetReplaceDict();
            
            string? mxcUrl = null;
            if (Config.PosterType == "attachment" && eventType != "upcoming")
            {
                mxcUrl = UploadImageToMatrix(item.PosterPath, false, config);
            }
            else if (!string.IsNullOrEmpty(item.ImageURL))
            {
                mxcUrl = UploadImageToMatrix(item.ImageURL, true, config);
            }

            if (!string.IsNullOrEmpty(mxcUrl))
            {
                replaceDict["{ImageURL}"] = mxcUrl;
            }

            foreach (var ele in replaceDict)
            {
                if (ele.Value is not null)
                {
                    tmpEntryHtml = this.TemplateReplace(tmpEntryHtml, ele.Key, ele.Value);
                }
            }

            string eventBadge = GetEventBadge(eventType);
            tmpEntryHtml = tmpEntryHtml.Replace("{EventBadge}", eventBadge, StringComparison.Ordinal);

            string itemUrl = string.IsNullOrEmpty(Config.Hostname) ? string.Empty : Config.Hostname;
            string entryHTML = tmpEntryHtml
                .Replace("{SeasonEpsInfo}", seaEpsHtml, StringComparison.Ordinal)
                .Replace("{ItemURL}", itemUrl, StringComparison.Ordinal);

            testHTML.Append(entryHTML);
        }

        return ReplaceBodyWithBuiltString(GetDefaultHTMLBody(config), testHTML.ToString());
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

    private string? UploadImageToMatrix(string? source, bool isUrl, MatrixConfiguration config)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        var homeserverUrl = config.HomeserverUrl.TrimEnd('/');

        string? cachedMxcUrl = GetCachedMxcUrl(source, homeserverUrl);
        if (!string.IsNullOrEmpty(cachedMxcUrl))
        {
            Logger.Debug($"Using cached MXC URL for {source} on {homeserverUrl}: {cachedMxcUrl}");
            return cachedMxcUrl;
        }

        try
        {
            Stream imageStream;
            string contentType;
            string fileName;

            Logger.Debug($"Preparing to upload Matrix image. Source: {source}, IsUrl: {isUrl}");

            if (isUrl)
            {
                Logger.Debug($"Downloading image from URL: {source}");
                var response = _httpClient.GetAsync(source).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error($"Failed to download image from {source}: {response.StatusCode}");
                    return null;
                }

                imageStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                var ext = contentType.ToLowerInvariant() switch
                {
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    "image/gif" => ".gif",
                    "image/svg+xml" => ".svg",
                    _ => ".jpeg"
                };
                fileName = "image" + ext;
                Logger.Debug($"Successfully downloaded image. ContentType: {contentType}, Ext: {ext}");
            }
            else
            {
                Logger.Debug($"Reading image from local file: {source}");
                if (!File.Exists(source))
                {
                    Logger.Debug($"Local file does not exist: {source}");
                    return null;
                }

                imageStream = File.OpenRead(source);
                var ext = Path.GetExtension(source).ToLowerInvariant();
                contentType = ext switch
                {
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    ".gif" => "image/gif",
                    ".svg" => "image/svg+xml",
                    _ => "image/jpeg"
                };
                fileName = Path.GetFileName(source);
                Logger.Debug($"Successfully opened local file. ContentType: {contentType}");
            }

            using (imageStream)
            {
                using var streamContent = new StreamContent(imageStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

                var requestUrl = $"{homeserverUrl}/_matrix/media/v3/upload?filename={Uri.EscapeDataString(fileName)}";

                Logger.Debug($"Sending Matrix upload POST request to: {requestUrl}");
                using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);
                request.Content = streamContent;

                var uploadResponse = _httpClient.SendAsync(request).GetAwaiter().GetResult();
                if (uploadResponse.IsSuccessStatusCode)
                {
                    var responseContent = uploadResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(responseContent);
                    var mxcUrl = jsonNode?["content_uri"]?.ToString();
                    Logger.Debug($"Matrix image upload successful. MXC URL: {mxcUrl}");
                    
                    if (!string.IsNullOrEmpty(mxcUrl))
                    {
                        SaveCachedMxcUrl(source, homeserverUrl, mxcUrl);
                    }

                    return mxcUrl;
                }
                else
                {
                    var errorBody = uploadResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Logger.Error($"Matrix Image Upload Failed for {source}: {uploadResponse.StatusCode} - {errorBody}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error uploading image to Matrix for {source}: {ex}");
        }

        return null;
    }

    private string? GetCachedMxcUrl(string source, string homeserver)
    {
        try
        {
            Db.CreateConnection();
            string escapedSource = source.Replace("'", "''", StringComparison.Ordinal);
            string escapedHomeserver = homeserver.Replace("'", "''", StringComparison.Ordinal);
            var query = $"SELECT MxcUrl FROM MatrixImageCache WHERE Source = '{escapedSource}' AND Homeserver = '{escapedHomeserver}';";
            
            foreach (var row in Db.Query(query))
            {
                if (row is not null && row.Count > 0)
                {
                    return row[0].ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error retrieving cached MXC URL for {source}: {ex}");
        }
        finally
        {
            Db.CloseConnection();
        }

        return null;
    }

    private void SaveCachedMxcUrl(string source, string homeserver, string mxcUrl)
    {
        try
        {
            Db.CreateConnection();
            string escapedSource = source.Replace("'", "''", StringComparison.Ordinal);
            string escapedHomeserver = homeserver.Replace("'", "''", StringComparison.Ordinal);
            string escapedMxcUrl = mxcUrl.Replace("'", "''", StringComparison.Ordinal);
            string query = $"INSERT OR REPLACE INTO MatrixImageCache (Source, Homeserver, MxcUrl) VALUES ('{escapedSource}', '{escapedHomeserver}', '{escapedMxcUrl}');";
            Db.ExecuteSQL(query);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error saving MXC URL to cache for {source}: {ex}");
        }
        finally
        {
            Db.CloseConnection();
        }
    }

    private string GetEventSectionHeader(string eventType, string libraryName = "Library")
    {
        var (title, emoji, color) = eventType.ToLowerInvariant() switch
        {
            "add" => ($"Added to {libraryName}", "🎬", "#4CAF50"),
            "update" => ($"Updated in {libraryName}", "🔄", "#2196F3"),
            "delete" => ($"Removed from {libraryName}", "🗑️", "#F44336"),
            "upcoming" => ($"Upcoming in {libraryName}", "📅", "#FF8C00"),
            _ => ($"Added to {libraryName}", "🎬", "#4CAF50")
        };

        return $"<h2><font data-mx-color='{color}'>{emoji} {title}</font></h2><hr/>";
    }

    private string GetEventBadge(string eventType)
    {
        var (label, emoji, bgColor) = eventType.ToLowerInvariant() switch
        {
            "add" => ("NEW", "🎬", "#4CAF50"),
            "update" => ("UPDATED", "🔄", "#2196F3"),
            "delete" => ("REMOVED", "🗑️", "#F44336"),
            "upcoming" => ("UPCOMING", "📅", "#FF8C00"),
            _ => ("NEW", "🎬", "#4CAF50")
        };

        return $"<font data-mx-color='{bgColor}'><b>[{emoji} {label}]</b></font>";
    }

    private void DefaultBodyAndEntry(MatrixConfiguration config)
    {
        this.matrixBodyHtml = config.Body ?? string.Empty;
        this.matrixEntryHtml = config.Entry ?? string.Empty;

        try
        {
            var pluginDir = Path.GetDirectoryName(typeof(MatrixMessageBuilder).Assembly.Location);
            if (pluginDir == null)
            {
                return;
            }
            
            string category = !string.IsNullOrEmpty(config.TemplateCategory) ? config.TemplateCategory : "Matrix";

            if (string.IsNullOrWhiteSpace(this.matrixBodyHtml))
            {
                this.matrixBodyHtml = File.ReadAllText(Path.Combine(pluginDir, "Templates", category, "template_body.html"));
            }

            if (string.IsNullOrWhiteSpace(this.matrixEntryHtml))
            {
                this.matrixEntryHtml = File.ReadAllText(Path.Combine(pluginDir, "Templates", category, "template_entry.html"));
            }
        }
        catch (Exception e)
        {
            Logger.Error("Failed to set default body HTML from template file: " + e);
        }
    }
}
