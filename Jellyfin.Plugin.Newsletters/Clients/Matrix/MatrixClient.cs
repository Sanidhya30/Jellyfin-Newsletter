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
    [HttpPost("SendMatrixTestMessage")]
    public void SendMatrixTestMessage([FromQuery] string configurationId)
    {
        if (string.IsNullOrEmpty(configurationId))
        {
            Logger.Error("Configuration ID is required for testing Matrix.");
            return;
        }

        var matrixConfig = Config.MatrixConfigurations.FirstOrDefault(c => c.Id == configurationId);

        if (matrixConfig == null)
        {
            Logger.Error($"Matrix configuration with ID '{configurationId}' not found.");
            return;
        }

        if (string.IsNullOrEmpty(matrixConfig.HomeserverUrl) ||
            string.IsNullOrEmpty(matrixConfig.AccessToken) ||
            string.IsNullOrEmpty(matrixConfig.RoomId))
        {
            Logger.Error($"Matrix configuration '{matrixConfig.Name}' is missing required fields (Homeserver, Token, or Room).");
            return;
        }

        try
        {
            var builder = new MatrixMessageBuilder(Logger, Db, LibraryManager, new List<JsonFileObj>());
            var htmlBody = builder.BuildMessageForTest(matrixConfig);
            string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    
            htmlBody = builder.TemplateReplace(htmlBody, "{ServerURL}", Config.Hostname);
            htmlBody = htmlBody.Replace("{Date}", currDate, StringComparison.Ordinal);

            SendToMatrixApi(matrixConfig, htmlBody);
        }
        catch (Exception e)
        {
            Logger.Error($"An error occurred while sending Matrix test message to '{matrixConfig.Name}': " + e);
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
                    if (string.IsNullOrEmpty(matrixConfig.HomeserverUrl) ||
                        string.IsNullOrEmpty(matrixConfig.AccessToken) ||
                        string.IsNullOrEmpty(matrixConfig.RoomId))
                    {
                        Logger.Info($"Matrix configuration '{matrixConfig.Name}' is missing required fields. Skipping.");
                        continue;
                    }

                    Logger.Debug($"Sending Matrix message to '{matrixConfig.Name}'!");

                    var builder = new MatrixMessageBuilder(Logger, Db, LibraryManager, matrixConfig.NewsletterOnUpcomingItemEnabled ? upcomingItems : Array.Empty<JsonFileObj>());
                    var htmlBody = builder.BuildMessageFromNewsletterData(applicationHost.SystemId, matrixConfig);
                    string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    
                    htmlBody = builder.TemplateReplace(htmlBody, "{ServerURL}", Config.Hostname);
                    htmlBody = htmlBody.Replace("{Date}", currDate, StringComparison.Ordinal);

                    bool result = SendToMatrixApi(matrixConfig, htmlBody);
                    anySuccess |= result;
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

    private bool SendToMatrixApi(MatrixConfiguration matrixConfig, string htmlBody)
    {
        try
        {
            var homeserverUrl = matrixConfig.HomeserverUrl.TrimEnd('/');
            // Need to URL encode the Room ID since it usually contains special characters like # or ! and colons
            var encodedRoomId = Uri.EscapeDataString(matrixConfig.RoomId);
            var txnId = Guid.NewGuid().ToString();

            var requestUrl = $"{homeserverUrl}/_matrix/client/v3/rooms/{encodedRoomId}/send/m.room.message/{txnId}";

            var payload = new MatrixPayload
            {
                MsgType = "m.text",
                Body = "Jellyfin Newsletter",
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
