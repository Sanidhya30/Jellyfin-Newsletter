#pragma warning disable 1591
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Newsletters.Clients.Discord;

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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient?.Dispose();
        }
    }

    [HttpPost("SendDiscordTestMessage")]
    public void SendDiscordTestMessage()
    {
        string webhookUrl = Config.DiscordWebhookURL;

        if (string.IsNullOrEmpty(webhookUrl))
        {
            Logger.Info("Discord webhook URL is not set. Aborting test message.");
            return;
        }

        try
        {
            EmbedBuilder builder = new(Logger, Db);
            List<Embed> embedList = builder.BuildEmbedForTest();

            var payload = new DiscordPayload
            {
                username = Config.DiscordWebhookName,
                embeds = embedList
            };

            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
            Logger.Debug("Sending Discord test message: " + jsonPayload);

            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = _httpClient.PostAsync(webhookUrl, content).GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode)
            {
                Logger.Debug("Discord test message sent successfully");
            }
            else
            {
                var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Logger.Error($"Discord test message failed: {response.StatusCode} - {error}");
            }
        }
        catch (Exception e)
        {
            Logger.Error("An error occurred while sending Discord test message: " + e);
        }
    }

    [HttpPost("SendDiscordMessage")]
    public bool SendDiscordMessage()
    {
        bool result = false;

        string webhookUrl = Config.DiscordWebhookURL;
        if (string.IsNullOrEmpty(webhookUrl))
        {
            Logger.Info("Discord webhook URL is not set. Aborting sending messages.");
            return result;
        }

        try
        {
            if (NewsletterDbIsPopulated())
            {
                Logger.Debug("Sending out Discord message!");

                EmbedBuilder builder = new(Logger, Db);
                var embedTuples = builder.BuildEmbedsFromNewsletterData(applicationHost.SystemId);

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

                    var embeds = chunk.Select(t => t.Item1).ToList();

                    var payload = new DiscordPayload
                    {
                        username = Config.DiscordWebhookName,
                        embeds = embeds
                    };

                    var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                    Logger.Debug("Sending discord message with payload: " + jsonPayload);

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
                        Logger.Debug("Discord message sent successfully");
                        result = true;
                    }
                    else
                    {
                        var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        Logger.Error($"Discord webhook failed: {response.StatusCode} - {error}");
                        result = false;
                        break;
                    }
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

        return result;
    }

    public bool Send()
    {
        return SendDiscordMessage();
    }
}