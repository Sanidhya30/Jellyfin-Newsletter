using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Integrations;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Newsletters.Clients.Matrix;

/// <summary>
/// Builds HTML content for Matrix newsletters based on templates.
/// Handles Matrix-specific image uploading and MXC URL caching.
/// </summary>
public class MatrixMessageBuilder(
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManager,
    IReadOnlyList<JsonFileObj> upcomingItems)
    : HtmlContentBuilder(loggerInstance, dbInstance, libraryManager, upcomingItems)
{
    private static readonly HttpClient _httpClient = new();
    private MatrixConfiguration? _currentConfig;

    /// <inheritdoc/>
    protected override string DefaultTemplateCategory => "Matrix";

    /// <summary>
    /// Builds the rich HTML message from the current newsletter data.
    /// </summary>
    /// <param name="serverId">The ID of the server.</param>
    /// <param name="config">The Matrix configuration.</param>
    /// <returns>A string containing the complete HTML body.</returns>
    public string BuildMessageFromNewsletterData(string serverId, MatrixConfiguration config)
    {
        EnsureSetup(config);

        var groupedItems = BuildGroupedItems(config, "Matrix");

        StringBuilder contentBuilderHtml = new();

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

                    contentBuilderHtml.Append(GetEventSectionHeader(eventGroup.EventType, library.LibraryName));

                    foreach (var item in library.Items)
                    {
                        contentBuilderHtml.Append(BuildEntryHtml(item, eventGroup.EventType, serverId));
                    }
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

        return ReplaceBodyWithBuiltString(GetDefaultHTMLBody(config), contentBuilderHtml.ToString());
    }

    /// <summary>
    /// Builds a sample message for testing.
    /// </summary>
    /// <param name="config">The Matrix configuration.</param>
    /// <returns>A string containing the complete HTML body for testing.</returns>
    public string BuildMessageForTest(MatrixConfiguration config)
    {
        EnsureSetup(config);
        return ReplaceBodyWithBuiltString(GetDefaultHTMLBody(config), BuildTestEntriesHtml(config));
    }

    /// <inheritdoc/>
    protected override void CustomizeItemReplaceDict(JsonFileObj item, string eventType, Dictionary<string, object?> replaceDict)
    {
        // Upload the image to the Matrix homeserver and use the MXC URL
        var matrixConfig = GetCurrentMatrixConfig();
        if (matrixConfig == null)
        {
            return;
        }

        string? mxcUrl = null;
        if (Config.PosterType == "attachment" && eventType != "upcoming")
        {
            mxcUrl = UploadImageToMatrix(item.PosterPath, false, matrixConfig);
        }
        else if (!string.IsNullOrEmpty(item.ImageURL))
        {
            mxcUrl = UploadImageToMatrix(item.ImageURL, true, matrixConfig);
        }

        if (!string.IsNullOrEmpty(mxcUrl))
        {
            replaceDict["{ImageURL}"] = mxcUrl;
        }
    }

    /// <inheritdoc/>
    protected override void CustomizeTestItemReplaceDict(JsonFileObj item, string eventType, Dictionary<string, object?> replaceDict, ITemplatedConfiguration config)
    {
        if (config is not MatrixConfiguration matrixConfig)
        {
            return;
        }

        string? mxcUrl = null;
        if (Config.PosterType == "attachment" && eventType != "upcoming")
        {
            mxcUrl = UploadImageToMatrix(item.PosterPath, false, matrixConfig);
        }
        else if (!string.IsNullOrEmpty(item.ImageURL))
        {
            mxcUrl = UploadImageToMatrix(item.ImageURL, true, matrixConfig);
        }

        if (!string.IsNullOrEmpty(mxcUrl))
        {
            replaceDict["{ImageURL}"] = mxcUrl;
        }
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

        return $"<font data-mx-color='{bgColor}'><b>[{emoji} {label}]</b></font>";
    }

    private MatrixConfiguration? GetCurrentMatrixConfig()
    {
        return _currentConfig;
    }

    // Override EnsureSetup to also store the config
    private new void EnsureSetup(ITemplatedConfiguration config)
    {
        if (config is MatrixConfiguration matrixConfig)
        {
            _currentConfig = matrixConfig;
        }

        base.EnsureSetup(config);
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
}
