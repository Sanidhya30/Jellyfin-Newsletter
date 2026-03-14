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
            replaceValue = "N/A";
        }

        if (replaceKey == "{RunTime}" && replaceValue is int rt && rt == 0)
        {
            replaceValue = "N/A";
        }

        if (replaceKey == "{CommunityRating}" && replaceValue is float rating)
        {
            replaceValue = rating > 0 ? rating.ToString($"F{Config.CommunityRatingDecimalPlaces}", CultureInfo.InvariantCulture) : "N/A";
        }

        return htmlObj.Replace(replaceKey, replaceValue.ToString(), StringComparison.Ordinal);
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
            // Fallback to HTTP ImageURL if upload fails or is unavailable
            var replaceDict = item.GetReplaceDict();
            string? mxcUrl = UploadImageToMatrix(item.PosterPath, config);
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

            string seaEpsHtml = "Season: 1 - Eps. 1 - 10<br>Season: 2 - Eps. 1 - 10";

            string tmpEntryHtml = matrixEntryHtml;
            var replaceDict = item.GetReplaceDict();
            
            string? mxcUrl = UploadImageToMatrix(item.PosterPath, config);
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

    private string? UploadImageToMatrix(string posterPath, MatrixConfiguration config)
    {
        if (string.IsNullOrEmpty(posterPath) || !File.Exists(posterPath))
        {
            return null;
        }

        try
        {
            var homeserverUrl = config.HomeserverUrl.TrimEnd('/');
            var fileName = Path.GetFileName(posterPath);
            var requestUrl = $"{homeserverUrl}/_matrix/media/v3/upload?filename={Uri.EscapeDataString(fileName)}";

            using var fileStream = File.OpenRead(posterPath);
            using var streamContent = new StreamContent(fileStream);

            var ext = Path.GetExtension(posterPath).ToLowerInvariant();
            var contentType = ext switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                _ => "image/jpeg"
            };
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);
            request.Content = streamContent;

            var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
            {
                var responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(responseContent);
                return jsonNode?["content_uri"]?.ToString();
            }
            else
            {
                var errorBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Logger.Error($"Matrix Image Upload Failed: {response.StatusCode} - {errorBody}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error uploading image to Matrix for {posterPath}: {ex}");
        }

        return null;
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

        return $@"
        <tr>
            <td colspan='2' style='padding: 20px 10px 10px 10px;'>
                <h2 style='color: {color}; margin: 0; font-size: 1.8em; border-bottom: 2px solid {color}; padding-bottom: 10px; display: flex; align-items: center; gap: 8px;'>
                   <span style='margin-right: 4px;'>{emoji}</span> {title}
                </h2>
            </td>
        </tr>";
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

        return $@"<span style='display: inline-block; background-color: {bgColor}; color: white; padding: 4px 8px; border-radius: 4px; font-size: 0.75em; font-weight: bold; margin-left: 8px;'>{emoji} {label}</span>";
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
            
            string category = !string.IsNullOrEmpty(config.TemplateCategory) ? config.TemplateCategory : "Modern";

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
