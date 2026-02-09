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
    SQLiteDatabase dbInstance)
    : Client(loggerInstance, dbInstance), IClient, IDisposable
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

        if (string.IsNullOrEmpty(discordConfig.WebhookURL))
        {
            Logger.Info($"Discord configuration '{discordConfig.Name}' has no webhook URL. Aborting test message.");
            return;
        }

        try
        {
            EmbedBuilder builder = new(Logger, Db);
            var embedList = builder.BuildEmbedForTest(discordConfig);

            var payload = new DiscordPayload
            {
                Username = discordConfig.WebhookName,
                Embeds = embedList
            };

            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
            Logger.Debug($"Sending Discord test message to '{discordConfig.Name}': " + jsonPayload);

            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = _httpClient.PostAsync(discordConfig.WebhookURL, content).GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode)
            {
                Logger.Debug($"Discord test message sent successfully to '{discordConfig.Name}'");
            }
            else
            {
                var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Logger.Error($"Discord test message failed for '{discordConfig.Name}': {response.StatusCode} - {error}");
            }
        }
        catch (Exception e)
        {
            Logger.Error($"An error occurred while sending Discord test message to '{discordConfig.Name}': " + e);
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
        bool result = false;
        string webhookUrl = discordConfig.WebhookURL;

        try
        {
            EmbedBuilder builder = new(Logger, Db);
            var embedTuples = builder.BuildEmbedsFromNewsletterData(applicationHost.SystemId, discordConfig);

            // Discord webhook does not support more than 10 embeds per message
            // Therefore, we're sending in chunks with atmost 10 embed in a payload.
            // For attachmenents, we will also send in chunks to avoid exceeding the limit i.e. 10 MB per message.
            int maxEmbedsPerMessage = 10;
            long maxTotalImageSize = 10 * 1024 * 1024; // 10MB

            int index = 0;

            while (index < embedTuples.Count)
            {
                var chunk = new List<(Embed, MemoryStream?, string)>();
                long currentTotalSize = 0;

                while (index < embedTuples.Count && chunk.Count < maxEmbedsPerMessage)
                {
                    var tuple = embedTuples[index];
                    long imageSize = 0;

                    // TODO: Can make this better, but even in case of tmdb url this will work as the ResizedImageStream will be null
                    imageSize = tuple.ResizedImageStream?.Length ?? 0;

                    if (currentTotalSize + imageSize > maxTotalImageSize)
                    {
                        break; // Stop adding to the chunk if it exceeds the max size
                    }

                    chunk.Add(tuple);
                    currentTotalSize += imageSize;
                    index++;
                }

                var payload = new DiscordPayload
                {
                    Username = discordConfig.WebhookName,
                    Embeds = new Collection<Embed>(chunk.Select(t => t.Item1).ToList()).AsReadOnly()
                };

                var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                Logger.Debug($"Sending discord message to '{discordConfig.Name}' with payload: " + jsonPayload);

                using var multipartContent = new MultipartFormDataContent();
                using var payloadContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                multipartContent.Add(payloadContent, "payload_json");

                if (Config.PosterType == "attachment")
                {
                    foreach (var (embed, resizedImageStream, uniqueImageName) in chunk)
                    {
                        if (resizedImageStream != null)
                        {
                            var fileContent = new ByteArrayContent(resizedImageStream.ToArray());
                            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                            multipartContent.Add(fileContent, uniqueImageName, uniqueImageName);
                        }
                    }
                }

                var response = _httpClient.PostAsync(webhookUrl, multipartContent).GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    Logger.Debug($"Discord message sent successfully to '{discordConfig.Name}'");
                    result = true;
                }
                else
                {
                    var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Logger.Error($"Discord webhook failed for '{discordConfig.Name}': {response.StatusCode} - {error}");
                    result = false;
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"An error has occured while sending to '{discordConfig.Name}': " + e);
        }

        return result;
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