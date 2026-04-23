using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Integrations;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Newsletters.Clients.Matrix;

/// <summary>
/// Represents a Matrix client for sending rich HTML messages.
/// </summary>
[Authorize(Policy = "RequiresElevation")]
[ApiController]
[Route("Matrix")]
public class MatrixClient(IServerApplicationHost appHost,
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManager,
    UpcomingMediaService upcomingMediaService)
    : Client(loggerInstance, dbInstance, libraryManager, upcomingMediaService), IClient, IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly IServerApplicationHost applicationHost = appHost;

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the underlying HttpClient.
    /// </summary>
    /// <param name="disposing">Whether it is disposing.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Sends a test Matrix message.
    /// </summary>
    /// <param name="configurationId">The configuration GUID to test.</param>
    /// <returns>An <see cref="ActionResult"/> indicating success or failure.</returns>
    [HttpPost("SendMatrixTestMessage")]
    public ActionResult SendMatrixTestMessage([FromQuery] string configurationId)
    {
        if (string.IsNullOrEmpty(configurationId))
        {
            Logger.Error("Configuration ID is required for testing Matrix.");
            return BadRequest("Configuration ID is required for testing Matrix.");
        }

        var matrixConfig = Config.MatrixConfigurations.FirstOrDefault(c => c.Id == configurationId);

        if (matrixConfig == null)
        {
            Logger.Error($"Matrix configuration with ID '{configurationId}' not found.");
            return BadRequest("Matrix configuration not found.");
        }

        if (!matrixConfig.IsEnabled)
        {
            Logger.Info($"Matrix configuration '{matrixConfig.Name}' is disabled. Aborting test message.");
            return BadRequest("Matrix configuration is disabled.");
        }

        if (string.IsNullOrEmpty(matrixConfig.HomeserverUrl) ||
            string.IsNullOrEmpty(matrixConfig.AccessToken) ||
            string.IsNullOrEmpty(matrixConfig.RoomId))
        {
            Logger.Error($"Matrix configuration '{matrixConfig.Name}' is missing required fields (Homeserver, Token, or Room).");
            return BadRequest("Matrix configuration is missing required fields (Homeserver, Token, or Room).");
        }

        // Split RoomId by comma
        var roomIds = matrixConfig.RoomId.Split(',')
                                     .Select(id => id.Trim())
                                     .Where(id => !string.IsNullOrEmpty(id))
                                     .ToList();

        if (roomIds.Count == 0)
        {
             Logger.Error($"Matrix configuration '{matrixConfig.Name}' has no valid room IDs. Aborting test message.");
             return BadRequest("Matrix configuration has no valid room IDs.");
        }

        try
        {
            var builder = new MatrixMessageBuilder(Logger, Db, LibraryManager, new List<JsonFileObj>());
            var htmlBody = builder.BuildMessageForTest(matrixConfig);
            htmlBody = builder.TemplateReplace(htmlBody, "{ServerURL}", Config.Hostname);
            htmlBody = builder.ReplaceDatePlaceholders(htmlBody);

            bool anySuccess = false;
            foreach (var roomId in roomIds)
            {
                bool success = SendToMatrixApi(matrixConfig, htmlBody, roomId);
                if (success)
                {
                    anySuccess = true;
                }
            }

            if (anySuccess)
            {
                return Ok("Test Matrix message sent successfully.");
            }
            else
            {
                return BadRequest("Failed to send test message to Matrix API. Check server logs for details.");
            }
        }
        catch (Exception e)
        {
            Logger.Error($"An error occurred while sending Matrix test message to '{matrixConfig.Name}': " + e);
            return BadRequest("An internal error occurred while sending the test message.");
        }
    }

    /// <summary>
    /// Sends the actual Matrix message.
    /// </summary>
    /// <returns>True if at least one message succeeded.</returns>
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
            if (hasData)
            {
                foreach (var matrixConfig in Config.MatrixConfigurations)
                {
                    if (!matrixConfig.IsEnabled)
                    {
                        Logger.Info($"Matrix configuration '{matrixConfig.Name}' is disabled. Skipping.");
                        continue;
                    }
                    
                    if (string.IsNullOrEmpty(matrixConfig.HomeserverUrl) ||
                        string.IsNullOrEmpty(matrixConfig.AccessToken) ||
                        string.IsNullOrEmpty(matrixConfig.RoomId))
                    {
                        Logger.Error($"Matrix configuration '{matrixConfig.Name}' is missing required fields. Skipping.");
                        continue;
                    }

                    var roomIds = matrixConfig.RoomId.Split(',')
                                                 .Select(id => id.Trim())
                                                 .Where(id => !string.IsNullOrEmpty(id))
                                                 .ToList();

                    if (roomIds.Count == 0)
                    {
                        Logger.Error($"Matrix configuration '{matrixConfig.Name}' has no valid room IDs. Skipping.");
                        continue;
                    }

                    Logger.Debug($"Sending Matrix message to '{matrixConfig.Name}'!");

                    var builder = new MatrixMessageBuilder(Logger, Db, LibraryManager, matrixConfig.NewsletterOnUpcomingItemEnabled ? upcomingItems : Array.Empty<JsonFileObj>());
                    var htmlBody = builder.BuildMessageFromNewsletterData(applicationHost.SystemId, matrixConfig);
                    htmlBody = builder.TemplateReplace(htmlBody, "{ServerURL}", Config.Hostname);
                    htmlBody = builder.ReplaceDatePlaceholders(htmlBody);

                    foreach (var roomId in roomIds)
                    {
                        bool result = SendToMatrixApi(matrixConfig, htmlBody, roomId);
                        anySuccess |= result;
                    }
                }
            }
            else
            {
                Logger.Info("There is no Newsletter data, nor any upcoming media. Have I scanned or sent out a Matrix newsletter recently?");
            }
        }
        catch (Exception e)
        {
            Logger.Error("An error has occurred in Matrix Client: " + e);
        }

        return anySuccess;
    }

    private bool SendToMatrixApi(MatrixConfiguration matrixConfig, string htmlBody, string roomId)
    {
        try
        {
            var homeserverUrl = matrixConfig.HomeserverUrl.TrimEnd('/');
            // Need to URL encode the Room ID since it usually contains special characters like # or ! and colons
            var encodedRoomId = Uri.EscapeDataString(roomId);
            var txnId = Guid.NewGuid().ToString();

            var requestUrl = $"{homeserverUrl}/_matrix/client/v3/rooms/{encodedRoomId}/send/m.room.message/{txnId}";

            var payload = new MatrixPayload
            {
                MsgType = "m.text",
                Body = "Jellyfin Newsletter (This message requires a Matrix client with HTML support to display correctly.)",
                Format = "org.matrix.custom.html",
                FormattedBody = htmlBody
            };

            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Put, requestUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", matrixConfig.AccessToken);
            request.Content = content;

            var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode)
            {
                Logger.Info($"Matrix message sent successfully to '{matrixConfig.Name}'");
                return true;
            }
            else
            {
                var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Logger.Error($"Matrix message failed for '{matrixConfig.Name}': {response.StatusCode} - {error}");
                Logger.Debug("Failed Matrix URL: " + requestUrl);
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error sending to Matrix API for '{matrixConfig.Name}': {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Send()
    {
        return SendMatrixMessage();
    }
}

/// <summary>
/// JSON payload representation for a Matrix message.
/// </summary>
public class MatrixPayload
{
    /// <summary>
    /// Gets or sets the message type.
    /// </summary>
    [JsonPropertyName("msgtype")]
    public string MsgType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the plaintext fallback body.
    /// </summary>
    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the custom format (e.g. org.matrix.custom.html).
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the rich formatted HTML body.
    /// </summary>
    [JsonPropertyName("formatted_body")]
    public string FormattedBody { get; set; } = string.Empty;
}
