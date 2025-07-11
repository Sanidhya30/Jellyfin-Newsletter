#pragma warning disable 1591
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Clients.CLIENT;
using Jellyfin.Plugin.Newsletters.Clients.Discord.EMBEDBuilder;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Newsletters.Clients.Discord.WEBHOOK;

[Authorize(Policy = Policies.RequiresElevation)]
[ApiController]
[Route("Discord")]
public class DiscordWebhook : Client, IClient, IDisposable
{
    private readonly HttpClient _httpClient;

    public DiscordWebhook(IServerApplicationHost applicationHost) : base(applicationHost)
    {
        _httpClient = new HttpClient();
    }

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
            EmbedBuilder builder = new EmbedBuilder();
            List<Embed> embedList = builder.BuildEmbedForTest();

            var payload = new DiscordPayload
            {
                username = Config.DiscordWebhookName,
                embeds = embedList
            };

            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
            Logger.Debug("Sending Discord test message: " + jsonPayload);

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

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
            Db.CreateConnection();

            if (NewsletterDbIsPopulated())
            {
                Logger.Debug("Sending out Discord message!");

                EmbedBuilder builder = new EmbedBuilder();
                var embedTuples = builder.BuildEmbedsFromNewsletterData(ApplicationHost.SystemId);

                // Discord webhook does not support more than 10 embeds per message
                // Therefore, we're sending in chunks with atmost 10 embed in a payload
                for (int i = 0; i < embedTuples.Count; i += 1)
                {
                    var chunk = embedTuples.Skip(i).Take(1).ToList();
                    var embeds = chunk.Select(t => t.embed).ToList();

                    var payload = new DiscordPayload
                    {
                        username = Config.DiscordWebhookName,
                        embeds = embeds
                    };

                    var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                    Logger.Debug("Sending discord message in chunks: " + jsonPayload);

                    var multipartContent = new MultipartFormDataContent();

                    multipartContent.Add(new StringContent(jsonPayload, Encoding.UTF8, "application/json"), "payload_json");

                    // Add the image attachments used in this chunk
                    foreach (var (embed, imagePath, uniqueFileName) in chunk)
                    {
                        if (imagePath != null)
                        {
                            if (System.IO.File.Exists(imagePath))
                            {
                                var imageBytes = System.IO.File.ReadAllBytes(imagePath);
                                var fileContent = new ByteArrayContent(imageBytes);
                                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

                                // Important: the name and fileName must match the attachment name used in the embed
                                multipartContent.Add(fileContent, uniqueFileName, uniqueFileName);
                            }
                            else
                            {
                                Logger.Warn($"Thumbnail file not found: {imagePath}");
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
        finally
        {
            Db.CloseConnection();
        }

        return result;
    }

    public bool Send()
    {
        return SendDiscordMessage();
    }
}