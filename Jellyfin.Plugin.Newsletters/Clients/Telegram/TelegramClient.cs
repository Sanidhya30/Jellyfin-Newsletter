using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Newsletters.Clients.Telegram;

/// <summary>
/// Represents a Telegram client for sending messages and test messages via Telegram Bot API.
/// </summary>
[Authorize(Policy = Policies.RequiresElevation)]
[ApiController]
[Route("Telegram")]
public class TelegramClient(IServerApplicationHost appHost,
    Logger loggerInstance,
    SQLiteDatabase dbInstance)
    : Client(loggerInstance, dbInstance), IClient, IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly IServerApplicationHost applicationHost = appHost;

    /// <summary>
    /// Releases the resources used by the <see cref="TelegramClient"/> class.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="TelegramClient"/> class.
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
    /// Sends a test message to the configured Telegram bot to verify connectivity and configuration.
    /// </summary>
    [HttpPost("SendTelegramTestMessage")]
    public void SendTelegramTestMessage()
    {
        string botToken = Config.TelegramBotToken;
        string chatId = Config.TelegramChatId;

        if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chatId))
        {
            Logger.Info("Telegram bot token or chat ID is not set. Aborting test message.");
            return;
        }

        try
        {
            TelegramMessageBuilder builder = new(Logger, Db);
            var testMessage = builder.BuildTestMessage();

            var payload = new
            {
                chat_id = chatId,
                text = testMessage,
                parse_mode = "MarkdownV2"
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            Logger.Debug("Sending Telegram test message: " + jsonPayload);

            var requestUri = $"https://api.telegram.org/bot{botToken}/sendMessage";
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = _httpClient.PostAsync(requestUri, content).GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode)
            {
                Logger.Debug("Telegram test message sent successfully");
            }
            else
            {
                var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Logger.Error($"Telegram test message failed: {response.StatusCode} - {error}");
            }
        }
        catch (Exception e)
        {
            Logger.Error("An error occurred while sending Telegram test message: " + e);
        }
    }

    /// <summary>
    /// Sends messages to the configured Telegram bot using newsletter data.
    /// </summary>
    /// <returns>True if the messages were sent successfully; otherwise, false.</returns>
    [HttpPost("SendTelegramMessage")]
    public bool SendTelegramMessage()
    {
        bool result = false;

        string botToken = Config.TelegramBotToken;
        string chatId = Config.TelegramChatId;

        if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chatId))
        {
            Logger.Info("Telegram bot token or chat ID is not set. Aborting sending messages.");
            return result;
        }

        try
        {
            if (NewsletterDbIsPopulated())
            {
                Logger.Debug("Sending out Telegram message!");

                TelegramMessageBuilder builder = new(Logger, Db);
                var messageTuples = builder.BuildMessagesFromNewsletterData(applicationHost.SystemId);

                // Telegram has a 4096 character limit per message
                // We'll send each item as a separate message
                foreach (var (messageText, imageUrl, imageStream, uniqueImageName) in messageTuples)
                {
                    bool messageSent = false;

                    // If image is enabled and available, send photo with caption
                    if (Config.TelegramThumbnailEnabled)
                    {
                        if (imageStream != null)
                        {
                            // Send as multipart file upload
                            messageSent = SendPhotoMessage(botToken, chatId, imageStream, messageText, uniqueImageName);
                        }
                        else if (!string.IsNullOrEmpty(imageUrl))
                        {
                            // Send using external URL
                            messageSent = SendPhotoWithUrl(botToken, chatId, imageUrl, messageText);
                        } 
                    }
                    else
                    {
                        // Otherwise send text message
                        messageSent = SendTextMessage(botToken, chatId, messageText);
                    }

                    if (messageSent)
                    {
                        result = true;
                        // Add small delay between messages to avoid rate limiting
                        Thread.Sleep(100);
                    }
                    else
                    {
                        result = false;
                        break;
                    }
                }
            }
            else
            {
                Logger.Info("There is no Newsletter data.. Have I scanned or sent out a Telegram newsletter recently?");
            }
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }

        return result;
    }

    /// <summary>
    /// Sends a text message to Telegram.
    /// </summary>
    /// <param name="botToken">The Telegram bot token.</param>
    /// <param name="chatId">The chat ID to send to.</param>
    /// <param name="messageText">The message text to send.</param>
    /// <returns>True if sent successfully; otherwise, false.</returns>
    private bool SendTextMessage(string botToken, string chatId, string messageText)
    {
        try
        {
            // Split message if it exceeds Telegram's 4096 character limit
            var messages = SplitMessage(messageText, 4096);

            foreach (var message in messages)
            {
                var payload = new
                {
                    chat_id = chatId,
                    text = message,
                    parse_mode = "MarkdownV2"
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                var requestUri = $"https://api.telegram.org/bot{botToken}/sendMessage";

                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = _httpClient.PostAsync(requestUri, content).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Logger.Error($"Telegram text message failed: {response.StatusCode} - {error}");
                    return false;
                }
            }

            Logger.Debug("Telegram text message sent successfully");
            return true;
        }
        catch (Exception e)
        {
            Logger.Error("Error sending Telegram text message: " + e);
            return false;
        }
    }

    /// <summary>
    /// Sends a photo message to Telegram with caption.
    /// </summary>
    /// <param name="botToken">The Telegram bot token.</param>
    /// <param name="chatId">The chat ID to send to.</param>
    /// <param name="imageStream">The image stream to send.</param>
    /// <param name="caption">The caption for the photo.</param>
    /// <param name="uniqueImageName">The unique name for the image.</param>
    /// <returns>True if sent successfully; otherwise, false.</returns>
    private bool SendPhotoMessage(string botToken, string chatId, MemoryStream imageStream, string caption, string uniqueImageName)
    {
        try
        {
            // Split caption if it exceeds Telegram's 1024 character limit for captions
            var captions = SplitMessage(caption, 1024);

            foreach (var cap in captions)
            {
                using var multipartContent = new MultipartFormDataContent();

                // Add the photo
                var fileContent = new ByteArrayContent(imageStream.ToArray());
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                multipartContent.Add(fileContent, "photo", uniqueImageName);

                // Add other form fields
                multipartContent.Add(new StringContent(chatId), "chat_id");
                multipartContent.Add(new StringContent(cap), "caption");
                multipartContent.Add(new StringContent("MarkdownV2"), "parse_mode");

                var requestUri = $"https://api.telegram.org/bot{botToken}/sendPhoto";
                var response = _httpClient.PostAsync(requestUri, multipartContent).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Logger.Error($"Telegram photo message failed: {response.StatusCode} - {error}");
                    return false;
                }
            }

            Logger.Debug("Telegram photo message sent successfully");
            return true;
        }
        catch (Exception e)
        {
            Logger.Error("Error sending Telegram photo message: " + e);
            return false;
        }
    }

    private bool SendPhotoWithUrl(string botToken, string chatId, string photoUrl, string caption)
    {
        try
        {
            var payload = new
            {
                chat_id = chatId,
                photo = photoUrl,  // Direct URL string
                caption = caption,
                parse_mode = "MarkdownV2"
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var requestUri = $"https://api.telegram.org/bot{botToken}/sendPhoto";
            
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = _httpClient.PostAsync(requestUri, content).GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode)
            {
                Logger.Debug("Telegram photo sent successfully via URL");
                return true;
            }
            else
            {
                var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Logger.Error($"Telegram sendPhoto failed: {response.StatusCode} - {error}");
                return false;
            }
        }
        catch (Exception e)
        {
            Logger.Error("Error sending photo with URL: " + e);
            return false;
        }
    }

    /// <summary>
    /// Splits a message into chunks that fit within the specified character limit.
    /// </summary>
    /// <param name="message">The message to split.</param>
    /// <param name="maxLength">The maximum length per chunk.</param>
    /// <returns>A list of message chunks.</returns>
    private List<string> SplitMessage(string message, int maxLength)
    {
        var chunks = new List<string>();

        if (string.IsNullOrEmpty(message) || message.Length <= maxLength)
        {
            chunks.Add(message);
            return chunks;
        }

        int startIndex = 0;
        while (startIndex < message.Length)
        {
            int endIndex = Math.Min(startIndex + maxLength, message.Length);

            // If we're not at the end and we're in the middle of the message,
            // try to break at a newline or space to avoid cutting words
            if (endIndex < message.Length)
            {
                // Look for a newline first
                int breakIndex = message.LastIndexOf('\n', endIndex - 1, Math.Min(maxLength, endIndex - startIndex));
                if (breakIndex == -1 || breakIndex < startIndex)
                {
                    // Look for a space
                    breakIndex = message.LastIndexOf(' ', endIndex - 1, Math.Min(maxLength, endIndex - startIndex));
                    if (breakIndex == -1 || breakIndex < startIndex)
                    {
                        // No good break point found, just break at maxLength
                        breakIndex = endIndex;
                    }
                    else
                    {
                        // Break at the space
                        breakIndex++; // Include the space in the first part
                    }
                }
                else
                {
                    // Break at the newline
                    breakIndex++;
                }

                chunks.Add(message.Substring(startIndex, breakIndex - startIndex));
                startIndex = breakIndex;
            }
            else
            {
                chunks.Add(message.Substring(startIndex));
                break;
            }
        }

        return chunks;
    }

    /// <summary>
    /// Sends a Telegram message using the configured bot and newsletter data.
    /// </summary>
    /// <returns>True if the message was sent successfully; otherwise, false.</returns>
    public bool Send()
    {
        return SendTelegramMessage();
    }
}