using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
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
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManager)
    : Client(loggerInstance, dbInstance, libraryManager), IClient, IDisposable
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
    /// Sends a test message to a specific Telegram configuration to verify connectivity.
    /// </summary>
    /// <param name="configurationId">The ID of the Telegram configuration to test.</param>
    [HttpPost("SendTelegramTestMessage")]
    public void SendTelegramTestMessage([FromQuery] string configurationId)
    {
        if (string.IsNullOrEmpty(configurationId))
        {
            Logger.Error("Configuration ID is required for testing.");
            return;
        }

        var telegramConfig = Config.TelegramConfigurations
            .FirstOrDefault(c => c.Id == configurationId);

        if (telegramConfig == null)
        {
            Logger.Error($"Telegram configuration with ID '{configurationId}' not found.");
            return;
        }

        if (string.IsNullOrEmpty(telegramConfig.BotToken) || string.IsNullOrEmpty(telegramConfig.ChatId))
        {
            Logger.Info($"Telegram configuration '{telegramConfig.Name}' has no bot token or chat ID. Aborting test message.");
            return;
        }

        // Split ChatId by comma
        var chatIds = telegramConfig.ChatId.Split(',')
                                     .Select(id => id.Trim())
                                     .Where(id => !string.IsNullOrEmpty(id))
                                     .ToList();

        if (chatIds.Count == 0)
        {
             Logger.Info($"Telegram configuration '{telegramConfig.Name}' has no valid chat IDs. Aborting test message.");
             return;
        }

        try
        {
            TelegramMessageBuilder builder = new(Logger, Db, LibraryManager);
            var (testMessage, imageUrl) = builder.BuildTestMessage(telegramConfig);
            
            if (string.IsNullOrEmpty(testMessage))
            {
                Logger.Error("Failed to build test message.");
                return;
            }

            bool anySuccess = false;

            foreach (var chatId in chatIds)
            {
                bool messageSent = false;
                if (telegramConfig.ThumbnailEnabled && !string.IsNullOrEmpty(imageUrl))
                {
                    // Send using external URL
                    messageSent = SendPhotoWithUrl(telegramConfig.BotToken, chatId, imageUrl, testMessage);
                }
                else
                {
                    // Otherwise send text message
                    messageSent = SendTextMessage(telegramConfig.BotToken, chatId, testMessage);
                }

                if (messageSent)
                {
                    Logger.Info($"Telegram test message sent successfully to '{telegramConfig.Name}' (ChatID: {chatId})");
                    anySuccess = true;
                }
                else
                {
                    Logger.Error($"Failed to send Telegram test message to '{telegramConfig.Name}' (ChatID: {chatId})");
                }
            }

            if (anySuccess)
            {
                Logger.Info($"Telegram test message process completed for '{telegramConfig.Name}'. At least one message verified.");
            }
            else
            {
                Logger.Error($"Telegram test message process failed for all Chat IDs in '{telegramConfig.Name}'.");
            }
        }
        catch (Exception e)
        {
            Logger.Error($"An error occurred while sending Telegram test message to '{telegramConfig.Name}': " + e);
        }
    }

    /// <summary>
    /// Sends messages to all configured Telegram bots using newsletter data.
    /// </summary>
    /// <returns>True if at least one message was sent successfully; otherwise, false.</returns>
    [HttpPost("SendTelegramMessage")]
    public bool SendTelegramMessage()
    {
        bool anySuccess = false;

        if (Config.TelegramConfigurations.Count == 0)
        {
            Logger.Info("No Telegram configurations found. Aborting sending messages.");
            return false;
        }

        try
        {
            if (NewsletterDbIsPopulated())
            {
                // Iterate over all Telegram configurations
                foreach (var telegramConfig in Config.TelegramConfigurations)
                {
                    if (string.IsNullOrEmpty(telegramConfig.BotToken) || string.IsNullOrEmpty(telegramConfig.ChatId))
                    {
                        Logger.Info($"Telegram configuration '{telegramConfig.Name}' has no bot token or chat ID. Skipping.");
                        continue;
                    }

                    Logger.Debug($"Sending Telegram message to '{telegramConfig.Name}'!");

                    bool result = SendToBot(telegramConfig);
                    anySuccess |= result;
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

        return anySuccess;
    }

    /// <summary>
    /// Sends newsletter data to a specific Telegram bot configuration.
    /// </summary>
    /// <param name="telegramConfig">The Telegram configuration to use.</param>
    /// <returns>True if the message was sent successfully; otherwise, false.</returns>
    private bool SendToBot(TelegramConfiguration telegramConfig)
    {
        bool anyResult = false;
        string botToken = telegramConfig.BotToken;

        var chatIds = telegramConfig.ChatId.Split(',')
                                     .Select(id => id.Trim())
                                     .Where(id => !string.IsNullOrEmpty(id))
                                     .ToList();

        if (chatIds.Count == 0)
        {
            Logger.Info($"Telegram configuration '{telegramConfig.Name}' has no valid chat IDs. Skipping.");
            return false;
        }

        try
        {
            TelegramMessageBuilder builder = new(Logger, Db, LibraryManager);
            var messageTuples = builder.BuildMessagesFromNewsletterData(applicationHost.SystemId, telegramConfig);

            // Telegram has a 4096 character limit per message
            // We'll send each item as a separate message

            foreach (var chatId in chatIds)
            {
                bool thisChatResult = true;
                Logger.Debug($"Sending Telegram newsletter to '{telegramConfig.Name}' (ChatID: {chatId})");
                
                foreach (var (messageText, imageUrl, imageStream, uniqueImageName) in messageTuples)
                {
                    bool messageSent = false;

                    // If image is enabled and available, send photo with caption
                    if (telegramConfig.ThumbnailEnabled)
                    {
                        if (imageStream != null)
                        {
                            // Reset stream position before reading
                            imageStream.Position = 0;

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
                        // Add small delay between messages to avoid rate limiting
                        Thread.Sleep(100);
                    }
                    else
                    {
                        thisChatResult = false;
                        break;
                    }
                }

                if (thisChatResult)
                {
                    anyResult = true;
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"An error has occured while sending to '{telegramConfig.Name}': " + e);
        }

        return anyResult;
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
                Logger.Debug("Sending Telegram text message: " + jsonPayload);
                
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
            // Telegram caption limit is 1024 characters
            // If caption exceeds limit, we'll send the photo with truncated caption
            // and follow up with text messages for the rest
            var requestUri = $"https://api.telegram.org/bot{botToken}/sendPhoto";
            
            string photoCaption = caption;
            string remainingText = string.Empty;
            
            if (caption.Length > 1024)
            {
                // Truncate caption and save remainder
                photoCaption = string.Concat(caption.AsSpan(0, 1020), "\\.\\.\\.");
                remainingText = caption.AsSpan(1020).ToString();
            }

            // Reset stream position to beginning
            imageStream.Position = 0;

            using var multipartContent = new MultipartFormDataContent();

            // Add chat_id as form field
            multipartContent.Add(new StringContent(chatId), "chat_id");

            // Add caption if provided
            if (!string.IsNullOrEmpty(photoCaption))
            {
                multipartContent.Add(new StringContent(photoCaption), "caption");
                multipartContent.Add(new StringContent("MarkdownV2"), "parse_mode");
            }

            // Add photo file
            var fileContent = new ByteArrayContent(imageStream.ToArray());
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            
            // The field name MUST be "photo" for sendPhoto
            multipartContent.Add(fileContent, "photo", uniqueImageName);

            Logger.Debug("Sending Telegram photo with caption: " + photoCaption);

            var response = _httpClient.PostAsync(requestUri, multipartContent).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Logger.Error($"Telegram photo message failed: {response.StatusCode} - {error}");
                return false;
            }

            Logger.Debug("Telegram photo message sent successfully");

            // If there's remaining text, send it as follow-up text messages
            if (!string.IsNullOrEmpty(remainingText))
            {
                Logger.Debug("Sending remaining caption text as separate message");
                Thread.Sleep(100); // Small delay before follow-up
                return SendTextMessage(botToken, chatId, remainingText);
            }

            return true;
        }
        catch (Exception e)
        {
            Logger.Error("Error sending Telegram photo message: " + e);
            return false;
        }
    }

    /// <summary>
    /// Sends a photo message to Telegram using an external URL.
    /// </summary>
    /// <param name="botToken">The Telegram bot token.</param>
    /// <param name="chatId">The chat ID to send to.</param>
    /// <param name="photoUrl">The URL of the photo to send.</param>
    /// <param name="caption">The caption for the photo.</param>
    /// <returns>True if sent successfully; otherwise, false.</returns>
    private bool SendPhotoWithUrl(string botToken, string chatId, string photoUrl, string caption)
    {
        try
        {
            // Telegram caption limit is 1024 characters
            string photoCaption = caption;
            string remainingText = string.Empty;
            
            if (caption.Length > 1024)
            {
                photoCaption = string.Concat(caption.AsSpan(0, 1020), "\\.\\.\\.");
                remainingText = caption.AsSpan(1020).ToString();
            }

            var payload = new
            {
                chat_id = chatId,
                photo = photoUrl,
                caption = photoCaption,
                parse_mode = "MarkdownV2"
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            Logger.Debug("Sending Telegram photo via URL: " + jsonPayload);
            
            var requestUri = $"https://api.telegram.org/bot{botToken}/sendPhoto";
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = _httpClient.PostAsync(requestUri, content).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Logger.Error($"Telegram sendPhoto via URL failed: {response.StatusCode} - {error}");
                return false;
            }

            Logger.Debug("Telegram photo sent successfully via URL");

            // Send remaining text if any
            if (!string.IsNullOrEmpty(remainingText))
            {
                Thread.Sleep(100);
                return SendTextMessage(botToken, chatId, remainingText);
            }

            return true;
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