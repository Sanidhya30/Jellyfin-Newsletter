using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Integrations;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Newsletters.Clients.Matrix;

/// <summary>
/// Matrix notification client. Sends HTML newsletters to Matrix rooms
/// via the Client-Server API.
/// </summary>
[Authorize(Policy = Policies.RequiresElevation)]
[ApiController]
[Route("Matrix")]
public class MatrixClient(
    IServerApplicationHost appHost,
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManager,
    UpcomingMediaService upcomingMediaService)
    : Client(loggerInstance, dbInstance, libraryManager, upcomingMediaService), IClient, IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly IServerApplicationHost _appHost = appHost;
    private bool _disposed;

    /// <summary>
    /// Sends a test message to verify Matrix configuration.
    /// </summary>
    /// <param name="configurationId">The configuration ID to test.</param>
    /// <returns>Action result.</returns>
    [HttpPost("SendMatrixTestMessage")]
    public async Task<ActionResult> SendMatrixTestMessage([FromQuery] string configurationId)
    {
        if (string.IsNullOrEmpty(configurationId))
        {
            Logger.Error("Configuration ID is required for testing.");
            return BadRequest("Configuration ID is required.");
        }

        var config = Config.MatrixConfigurations
            .FirstOrDefault(c => c.Id == configurationId);

        if (config == null)
        {
            Logger.Error($"Matrix configuration with ID '{configurationId}' not found.");
            return BadRequest("Configuration not found.");
        }

        if (!ValidateConfig(config))
        {
            Logger.Info($"Matrix configuration '{config.Name}' is incomplete. Aborting test message.");
            return BadRequest("Matrix configuration is incomplete. Homeserver URL, access token, and room IDs are required.");
        }

        try
        {
            var builder = new MatrixMessageBuilder(Logger, Db, LibraryManager, Array.Empty<JsonFileObj>());
            var (html, plainText) = builder.BuildTestMessage(config);

            // Upload test poster images and replace placeholders with mxc:// URLs
            if (config.ThumbnailEnabled)
            {
                for (int i = 0; i < MatrixMessageBuilder.TestPosterUrls.Count; i++)
                {
                    var (title, url) = MatrixMessageBuilder.TestPosterUrls[i];
                    var placeholder = $"{{{{POSTER:{i}}}}}";
                    var mxcUrl = await DownloadAndUploadImage(config, url, title).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(mxcUrl))
                    {
                        html = html.Replace(placeholder, $"<img src=\"{mxcUrl}\" alt=\"\" width=\"100\">", StringComparison.Ordinal);
                    }
                    else
                    {
                        html = html.Replace(placeholder, string.Empty, StringComparison.Ordinal);
                    }
                }
            }

            var success = await SendToRooms(config, html, plainText).ConfigureAwait(false);

            if (success)
            {
                Logger.Info($"Matrix test message sent successfully to '{config.Name}'.");
                return Ok("Test message sent.");
            }
            else
            {
                Logger.Error($"Failed to send Matrix test message to '{config.Name}'.");
                return StatusCode(500, "Failed to send test message. Check server logs.");
            }
        }
        catch (Exception e)
        {
            Logger.Error($"An error occurred while sending Matrix test message to '{config.Name}': " + e);
            return StatusCode(500, "Failed to send test message. Check server logs.");
        }
    }

    /// <summary>
    /// Sends the newsletter to all Matrix configurations.
    /// </summary>
    /// <returns>True if at least one configuration sent successfully.</returns>
    [HttpPost("SendMatrixMessage")]
    public bool SendMatrixMessage()
    {
        bool anySuccess = false;

        if (Config.MatrixConfigurations.Count == 0)
        {
            Logger.Info("No Matrix configurations found. Aborting sending messages.");
            return false;
        }

        try
        {
            var (hasData, upcomingItems) = HasDataToSendAsync().GetAwaiter().GetResult();
            if (!hasData)
            {
                Logger.Info("Matrix: no newsletter data to send.");
                return false;
            }

            foreach (var matrixConfig in Config.MatrixConfigurations)
            {
                if (!ValidateConfig(matrixConfig))
                {
                    Logger.Info($"Matrix configuration '{matrixConfig.Name}' is incomplete, skipping.");
                    continue;
                }

                Logger.Debug($"Sending Matrix message to '{matrixConfig.Name}'!");

                IReadOnlyList<JsonFileObj> builderItems = matrixConfig.NewsletterOnUpcomingItemEnabled
                    ? upcomingItems
                    : Array.Empty<JsonFileObj>();
                var builder = new MatrixMessageBuilder(Logger, Db, LibraryManager, builderItems);
                var systemId = _appHost.SystemId;
                var itemMessages = builder.BuildMessagesFromNewsletterData(systemId, matrixConfig);

                if (itemMessages.Count == 0)
                {
                    Logger.Info($"Matrix config '{matrixConfig.Name}': no items matched filters.");
                    continue;
                }

                // Upload images and build full newsletter HTML
                var (fullHtml, fullPlainText) = BuildFullNewsletter(matrixConfig, itemMessages);

                var success = SendToRooms(matrixConfig, fullHtml, fullPlainText)
                    .GetAwaiter().GetResult();

                if (success)
                {
                    anySuccess = true;
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }

        return anySuccess;
    }

    /// <inheritdoc />
    public bool Send()
    {
        return SendMatrixMessage();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="MatrixClient"/> class.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _httpClient?.Dispose();
        }

        _disposed = true;
    }

    private static bool ValidateConfig(MatrixConfiguration config)
    {
        return !string.IsNullOrWhiteSpace(config.HomeserverUrl)
            && !string.IsNullOrWhiteSpace(config.AccessToken)
            && !string.IsNullOrWhiteSpace(config.RoomIds);
    }

    private static string NormalizeHomeserverUrl(string url)
    {
        return url.TrimEnd('/');
    }

    private static string ValidateMsgType(string msgType)
    {
        return msgType is "m.text" or "m.notice" ? msgType : "m.text";
    }

    private (string Html, string PlainText) BuildFullNewsletter(
        MatrixConfiguration config,
        System.Collections.ObjectModel.ReadOnlyCollection<(string Html, string PlainText, string? ImagePath, string UniqueImageName)> itemMessages)
    {
        var html = new StringBuilder();
        var plain = new StringBuilder();

        html.Append("<h2>🎬 Jellyfin Newsletter</h2>");
        plain.AppendLine("Jellyfin Newsletter");
        plain.AppendLine("===");

        foreach (var (itemHtml, itemPlain, imagePath, uniqueName) in itemMessages)
        {
            html.Append("<hr>");
            plain.AppendLine("---");

            // Upload poster image if available
            string? mxcUrl = null;
            if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
            {
                mxcUrl = UploadImage(config, imagePath!, uniqueName)
                    .GetAwaiter().GetResult();
            }

            // Table layout: poster left, details right
            if (!string.IsNullOrEmpty(mxcUrl))
            {
                html.Append("<table><tr>");
                html.Append(CultureInfo.InvariantCulture, $"<td valign=\"top\"><img src=\"{mxcUrl}\" alt=\"\" width=\"100\"></td>");
                html.Append("<td valign=\"top\">");
                html.Append(itemHtml);
                html.Append("</td>");
                html.Append("</tr></table>");
            }
            else
            {
                html.Append(itemHtml);
            }

            plain.Append(itemPlain);
        }

        html.Append("<hr>");
        html.Append("<p>🍿 <i>Sent from Jellyfin</i></p>");
        plain.AppendLine("---");
        plain.AppendLine("Sent from Jellyfin");

        return (html.ToString(), plain.ToString());
    }

    private async Task<string?> DownloadAndUploadImage(MatrixConfiguration config, string imageUrl, string filename)
    {
        var baseUrl = NormalizeHomeserverUrl(config.HomeserverUrl);
        var safeFilename = Uri.EscapeDataString(filename + ".jpg");

        try
        {
            var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl).ConfigureAwait(false);
            var content = new ByteArrayContent(imageBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl}/_matrix/media/v3/upload?filename={safeFilename}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);
            request.Content = content;

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warn($"Matrix image upload failed ({response.StatusCode}): {responseBody}");
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.GetProperty("content_uri").GetString();
        }
        catch (Exception e)
        {
            Logger.Warn($"Matrix test image download/upload error for {filename}: {e.Message}");
            return null;
        }
    }

    private async Task<string?> UploadImage(MatrixConfiguration config, string imagePath, string filename)
    {
        var baseUrl = NormalizeHomeserverUrl(config.HomeserverUrl);
        var safeFilename = Uri.EscapeDataString(filename + ".jpg");

        try
        {
            var imageBytes = await System.IO.File.ReadAllBytesAsync(imagePath).ConfigureAwait(false);
            var content = new ByteArrayContent(imageBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl}/_matrix/media/v3/upload?filename={safeFilename}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);
            request.Content = content;

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warn($"Matrix image upload failed ({response.StatusCode}): {responseBody}");
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.GetProperty("content_uri").GetString();
        }
        catch (Exception e)
        {
            Logger.Warn($"Matrix image upload error for {filename}: {e.Message}");
            return null;
        }
    }

    private async Task<bool> SendToRooms(MatrixConfiguration config, string html, string plainText)
    {
        var baseUrl = NormalizeHomeserverUrl(config.HomeserverUrl);
        var msgType = ValidateMsgType(config.MsgType);
        var roomIds = config.RoomIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var anySuccess = false;

        foreach (var roomId in roomIds)
        {
            // Attempt to join the room (accepts pending invite, no-ops if already joined)
            await TryJoinRoom(config, baseUrl, roomId).ConfigureAwait(false);

            // Send the message
            var txnId = Guid.NewGuid().ToString();
            var encodedRoomId = Uri.EscapeDataString(roomId);
            var url = $"{baseUrl}/_matrix/client/v3/rooms/{encodedRoomId}/send/m.room.message/{txnId}";

            var payload = JsonSerializer.Serialize(new
            {
                msgtype = msgType,
                format = "org.matrix.custom.html",
                body = plainText,
                formatted_body = html,
            });

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Put, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    Logger.Info($"Matrix message sent to {roomId}");
                    anySuccess = true;
                }
                else
                {
                    Logger.Error($"Matrix send to {roomId} failed ({response.StatusCode}): {responseBody}");
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Matrix send to {roomId} error: {e.Message}");
            }
        }

        return anySuccess;
    }

    private async Task TryJoinRoom(MatrixConfiguration config, string baseUrl, string roomId)
    {
        var encodedRoomId = Uri.EscapeDataString(roomId);
        var url = $"{baseUrl}/_matrix/client/v3/join/{encodedRoomId}";

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Logger.Warn($"Matrix join {roomId} failed ({response.StatusCode}): {body}. "
                    + "Make sure the bot has been invited to this room.");
            }
        }
        catch (Exception e)
        {
            Logger.Warn($"Matrix join {roomId} error: {e.Message}");
        }
    }
}
