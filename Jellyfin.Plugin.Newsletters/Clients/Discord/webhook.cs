using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Newsletters.Clients.Discord;

/// <summary>
/// Represents a Discord webhook client for sending messages and test messages to Discord via webhooks.
/// </summary>
[Authorize(Policy = Policies.RequiresElevation)]
[ApiController]
[Route("Discord")]
public class DiscordWebhook(IServerApplicationHost appHost,
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManager)
    : Client(loggerInstance, dbInstance, libraryManager), IClient, IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly IServerApplicationHost applicationHost = appHost;

    /// <summary>
    /// Releases the resources used by the <see cref="DiscordWebhook"/> class.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="DiscordWebhook"/> class.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Sends a test message to a specific Discord webhook configuration to verify connectivity.
    /// </summary>
    /// <param name="configurationId">The ID of the Discord configuration to test.</param>
    [HttpPost("SendDiscordTestMessage")]
    public void SendDiscordTestMessage([FromQuery] string configurationId)
    {
        if (string.IsNullOrEmpty(configurationId))
        {
            Logger.Error("Configuration ID is required for testing.");
            return;
        }

        var discordConfig = Config.DiscordConfigurations
            .FirstOrDefault(c => c.Id == configurationId);

        if (discordConfig == null)
        {
            Logger.Error($"Discord configuration with ID '{configurationId}' not found.");
            return;
        }

        // Split the Webhook URL by comma to support multiple webhooks
        var webhookUrls = discordConfig.WebhookURL.Split(',')
                                       .Select(url => url.Trim())
                                       .Where(url => !string.IsNullOrEmpty(url))
                                       .ToList();

        if (webhookUrls.Count == 0)
        {
            Logger.Info($"Discord configuration '{discordConfig.Name}' has no valid webhook URLs. Aborting test message.");
            return;
        }

        bool anySuccess = false;

        foreach (var webhookUrl in webhookUrls)
        {
            try
            {
                EmbedBuilder builder = new(Logger, Db, LibraryManager);
                var embedList = builder.BuildEmbedForTest(discordConfig);

                var payload = new DiscordPayload
                {
                    Username = discordConfig.WebhookName,
                    Embeds = embedList
                };

                var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                Logger.Debug($"Sending Discord test message to '{discordConfig.Name}' (URL: {webhookUrl}): " + jsonPayload);

                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = _httpClient.PostAsync(webhookUrl, content).GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    Logger.Debug($"Discord test message sent successfully to '{discordConfig.Name}' (URL: {webhookUrl})");
                    anySuccess = true;
                }
                else
                {
                    var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Logger.Error($"Discord test message failed for '{discordConfig.Name}' (URL: {webhookUrl}): {response.StatusCode} - {error}");
                }
            }
            catch (Exception e)
            {
                Logger.Error($"An error occurred while sending Discord test message to '{discordConfig.Name}' (URL: {webhookUrl}): " + e);
            }
        }

        if (anySuccess)
        {
            Logger.Info($"Discord test message process completed for '{discordConfig.Name}'. At least one message verified.");
        }
        else
        {
            Logger.Error($"Discord test message process failed for all webhooks in '{discordConfig.Name}'.");
        }
    }

    /// <summary>
    /// Sends a message to all configured Discord webhooks using newsletter data.
    /// </summary>
    /// <returns>True if at least one message was sent successfully; otherwise, false.</returns>
    [HttpPost("SendDiscordMessage")]
    public bool SendDiscordMessage()
    {
        bool anySuccess = false;

        if (Config.DiscordConfigurations.Count == 0)
        {
            Logger.Info("No Discord configurations found. Aborting sending messages.");
            return false;
        }

        try
        {
            if (NewsletterDbIsPopulated())
            {
                // Iterate over all Discord configurations
                foreach (var discordConfig in Config.DiscordConfigurations)
                {
                    if (string.IsNullOrEmpty(discordConfig.WebhookURL))
                    {
                        Logger.Info($"Discord configuration '{discordConfig.Name}' has no webhook URL. Skipping.");
                        continue;
                    }

                    Logger.Debug($"Sending Discord message to '{discordConfig.Name}'!");

                    bool result = SendToWebhook(discordConfig);
                    anySuccess |= result;
                }
            }
            else
            {
                Logger.Info("There is no Newsletter data.. Have I scanned or sent out a discord newsletter recently?");
            }
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }

        return anySuccess;
    }

    /// <summary>
    /// Sends newsletter data to a specific Discord webhook configuration.
    /// </summary>
    /// <param name="discordConfig">The Discord configuration to use.</param>
    /// <returns>True if the message was sent successfully; otherwise, false.</returns>
    private bool SendToWebhook(DiscordConfiguration discordConfig)
    {
        bool anyResult = false; // true if at least one chunk was sent successfully across all webhooks
        
        // Split the Webhook URL by comma to support multiple webhooks
        var webhookUrls = discordConfig.WebhookURL.Split(',')
                                       .Select(url => url.Trim())
                                       .Where(url => !string.IsNullOrEmpty(url))
                                       .ToList();

        if (webhookUrls.Count == 0)
        {
            Logger.Info($"Discord configuration '{discordConfig.Name}' has no valid webhook URLs. Skipping.");
            return false;
        }

        try
        {
            EmbedBuilder builder = new(Logger, Db, LibraryManager);
            var embedTuples = builder.BuildEmbedsFromNewsletterData(applicationHost.SystemId, discordConfig);

            // Discord webhook does not support more than 10 embeds per message
            // Therefore, we're sending in chunks with atmost 10 embed in a payload.
            // For attachmenents, we will also send in chunks to avoid exceeding the limit i.e. 10 MB per message.
            int maxEmbedsPerMessage = 10;
            long maxTotalImageSize = 10 * 1024 * 1024; // 10MB

            int index = 0;
            var chunks = new List<List<(Embed, MemoryStream?, string)>>();

            // Pre-calculate chunks based on embeds and size limits
            // This is done once for the newsletter data
            while (index < embedTuples.Count)
            {
                var chunk = new List<(Embed, MemoryStream?, string)>();
                long currentTotalSize = 0;

                while (index < embedTuples.Count && chunk.Count < maxEmbedsPerMessage)
                {
                    var tuple = embedTuples[index];
                    long imageSize = tuple.ResizedImageStream?.Length ?? 0;

                    if (currentTotalSize + imageSize > maxTotalImageSize && chunk.Count > 0)
                    {
                        break; // Stop adding to the chunk if it exceeds the max size
                    }

                    chunk.Add(tuple);
                    currentTotalSize += imageSize;
                    index++;
                }

                chunks.Add(chunk);
            }

            // Iterate over each webhook URL and send the pre-calculated chunks
            foreach (var webhookUrl in webhookUrls)
            {
                Logger.Debug($"Sending Discord newsletter to '{discordConfig.Name}' (URL: {webhookUrl})");

                foreach (var chunk in chunks)
                {
                    try 
                    {
                        var payload = new DiscordPayload
                        {
                            Username = discordConfig.WebhookName,
                            Embeds = new Collection<Embed>(chunk.Select(t => t.Item1).ToList()).AsReadOnly()
                        };

                        var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                        Logger.Debug($"Sending discord message chunk to '{discordConfig.Name}' (URL: {webhookUrl})");

                        using var multipartContent = new MultipartFormDataContent();
                        using var payloadContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                        multipartContent.Add(payloadContent, "payload_json");

                        if (Config.PosterType == "attachment")
                        {
                            foreach (var (embed, resizedImageStream, uniqueImageName) in chunk)
                            {
                                if (resizedImageStream != null)
                                {
                                    // MemoryStream is reusable if position is reset, but ByteArrayContent takes a copy or byte array.
                                    // We used 'resizedImageStream.ToArray()' effectively creating a copy.
                                    var fileContent = new ByteArrayContent(resizedImageStream.ToArray());
                                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                                    multipartContent.Add(fileContent, uniqueImageName, uniqueImageName);
                                }
                            }
                        }

                        var response = _httpClient.PostAsync(webhookUrl, multipartContent).GetAwaiter().GetResult();

                        if (response.IsSuccessStatusCode)
                        {
                            // Track any successful chunk
                            anyResult = true;
                            Logger.Debug($"Discord message chunk sent successfully to '{discordConfig.Name}' (URL: {webhookUrl})");
                        }
                        else
                        {
                            var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            Logger.Error($"Discord webhook failed for '{discordConfig.Name}' (URL: {webhookUrl}): {response.StatusCode} - {error} - Continuing to next chunk.");
                        }
                    }
                    catch (Exception ex)
                    {
                         Logger.Error($"Error sending chunk to '{discordConfig.Name}' (URL: {webhookUrl}): {ex.Message} - Continuing to next chunk.");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"An error has occured while preparing/sending to '{discordConfig.Name}': " + e);
        }

        return anyResult;
    }

    /// <summary>
    /// Sends a Discord message using the configured webhooks and newsletter data.
    /// </summary>
    /// <returns>True if at least one message was sent successfully; otherwise, false.</returns>
    public bool Send()
    {
        return SendDiscordMessage();
    }
}