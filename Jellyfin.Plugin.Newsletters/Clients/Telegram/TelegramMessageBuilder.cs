using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;

namespace Jellyfin.Plugin.Newsletters.Clients.Telegram;

/// <summary>
/// Builds Telegram messages from newsletter data.
/// </summary>
public class TelegramMessageBuilder(Logger loggerInstance,
    SQLiteDatabase dbInstance) : ClientBuilder(loggerInstance, dbInstance)
{
    /// <summary>
    /// Builds a test message for Telegram.
    /// </summary>
    /// <param name="telegramConfig">The Telegram configuration to use.</param>
    /// <returns>A tuple containing the formatted test message string and the image URL.</returns>
    public (string MessageText, string? ImageUrl) BuildTestMessage(TelegramConfiguration telegramConfig)
    {
        JsonFileObj item = JsonFileObj.GetTestObj();
        
        try
        {
            Db.CreateConnection();
            string messageText = BuildMessageText(item, "newsletter-test", telegramConfig);
            string? imageUrl = item.ImageURL;
            
            return (messageText, imageUrl);
        }
        catch (Exception e)
        {
            Logger.Error("Error building test message: " + e);
            return (string.Empty, null);
        }
        finally
        {
            Db.CloseConnection();
        }
    }

    /// <summary>
    /// Builds Telegram messages from newsletter data.
    /// </summary>
    /// <param name="systemId">The Jellyfin system ID.</param>
    /// <param name="telegramConfig">The Telegram configuration to use.</param>
    /// <returns>A collection of message tuples containing text and optional image data.</returns>
    public ReadOnlyCollection<(string MessageText, string? ImageUrl, MemoryStream? ImageStream, string UniqueImageName)> BuildMessagesFromNewsletterData(string systemId, TelegramConfiguration telegramConfig)
    {
        var completed = new HashSet<string>(); // Store "Title_EventType"
        var result = new List<(string, string?, MemoryStream?, string)>();

        try
        {
            Db.CreateConnection();

            foreach (var row in Db.Query("SELECT * FROM CurrNewsletterData;"))
            {
                if (row is not null)
                {
                    JsonFileObj item = JsonFileObj.ConvertToObj(row);
                    string eventType = item.EventType?.ToLowerInvariant() ?? "add";

                    // Check if the event type should be included based on configuration
                    if (!ShouldIncludeItem(item, telegramConfig, "Telegram"))
                    {
                        continue;
                    }

                    // Create a unique key combining title and event type
                    string uniqueKey = $"{item.Title}_{eventType}";
                    if (completed.Contains(uniqueKey))
                    {
                        continue;
                    }

                    var messageText = BuildMessageText(item, systemId, telegramConfig);
                    
                    string? imageUrl = null;
                    MemoryStream? resizedImageStream = null;
                    string uniqueImageName = string.Empty;

                    if (telegramConfig.ThumbnailEnabled)
                    {
                        if (Config.PosterType == "attachment")
                        {
                            // Upload as multipart file
                            (resizedImageStream, uniqueImageName, var success) = ResizeImage(item.PosterPath);
                        }
                        else
                        {
                            // Use external URL directly
                            imageUrl = item.ImageURL;
                        }
                    }

                    result.Add((messageText, imageUrl, resizedImageStream, uniqueImageName));
                    completed.Add(uniqueKey);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error("Error building Telegram messages: " + e);
        }
        finally
        {
            Db.CloseConnection();
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// Builds the message text for a specific item.
    /// </summary>
    /// <param name="item">The newsletter item.</param>
    /// <param name="systemId">The Jellyfin system ID.</param>
    /// <param name="telegramConfig">The Telegram configuration to use.</param>
    /// <returns>The formatted message text.</returns>
    private string BuildMessageText(JsonFileObj item, string systemId, TelegramConfiguration telegramConfig)
    {
        var messageBuilder = new System.Text.StringBuilder();

        // Get event type prefix
        string eventType = item.EventType?.ToLowerInvariant() ?? "add";
        string eventPrefix = GetEventDescriptionPrefix(eventType);
        
        // Add title with Jellyfin link if hostname is configured
        if (!string.IsNullOrEmpty(Config.Hostname) && !string.IsNullOrEmpty(item.ItemID))
        {
            string jellyfinUrl;
            if (systemId == "newsletter-test")
            {
                jellyfinUrl = Config.Hostname;
            }
            else
            {
                jellyfinUrl = $"{Config.Hostname}/web/index.html#/details?id={item.ItemID}&serverId={systemId}&event={eventType}";
            }
            
            messageBuilder.AppendLine(CultureInfo.InvariantCulture, $"*[{EscapeMarkdown(item.Title)}]({jellyfinUrl})*");
        }
        else
        {
            messageBuilder.AppendLine(CultureInfo.InvariantCulture, $"*{EscapeMarkdown(item.Title)}*");
        }

        messageBuilder.AppendLine(EscapeMarkdown(eventPrefix));
        messageBuilder.AppendLine();
        
        // Add description if enabled
        if (telegramConfig.DescriptionEnabled && !string.IsNullOrEmpty(item.SeriesOverview))
        {
            messageBuilder.AppendLine(EscapeMarkdown(item.SeriesOverview));
        }

        messageBuilder.AppendLine();

        // Add series/episode information if available
        if (item.Type == "Series" && telegramConfig.EpisodesEnabled)
        {
            string seaEps;
            if (item.Title == "Newsletter-Test")
            {
                // For test object, create hardcoded episode info
                var testSeaEps = new System.Text.StringBuilder();
                testSeaEps.AppendLine("Season: 1 - Eps. 1 - 10");
                testSeaEps.AppendLine("Season: 2 - Eps. 1 - 10");
                testSeaEps.AppendLine("Season: 3 - Eps. 1 - 10");
                seaEps = testSeaEps.ToString();
            }
            else
            {
                ReadOnlyCollection<NlDetailsJson> parsedInfoList = ParseSeriesInfo(item);
                seaEps = GetSeasonEpisode(parsedInfoList);
            }
            
            if (!string.IsNullOrWhiteSpace(seaEps))
            {
                messageBuilder.AppendLine("*Episodes:*");
                messageBuilder.AppendLine(EscapeMarkdown(seaEps.TrimEnd('\n')));
                messageBuilder.AppendLine();
            }
        }

        // Add metadata fields
        var metadataParts = new List<string>();

        if (telegramConfig.RatingEnabled)
        {
            // var ratingString = item.CommunityRating?.ToString($"F{Config.CommunityRatingDecimalPlaces}", CultureInfo.InvariantCulture) ?? "N/A";
            messageBuilder.AppendLine(CultureInfo.InvariantCulture, $"Rating: {EscapeMarkdown(item.CommunityRating?.ToString($"F{Config.CommunityRatingDecimalPlaces}", CultureInfo.InvariantCulture) ?? "N/A")}");
        }

        if (telegramConfig.PGRatingEnabled)
        {
            messageBuilder.AppendLine(CultureInfo.InvariantCulture, $"PG Rating: {EscapeMarkdown(item.OfficialRating ?? "N/A")}");
        }

        if (telegramConfig.DurationEnabled)
        {
            messageBuilder.AppendLine(CultureInfo.InvariantCulture, $"Duration: {item.RunTime} min");
        }

        return messageBuilder.ToString().Trim();
    }

    private string GetSeasonEpisode(IReadOnlyCollection<NlDetailsJson> list)
    {
        return GetSeasonEpisodeBase(list);
    }

    /// <summary>
    /// Gets the description prefix for the message based on the event type.
    /// </summary>
    /// <param name="eventType">The event type (add, delete, update).</param>
    /// <returns>The formatted description prefix with emoji.</returns>
    private string GetEventDescriptionPrefix(string? eventType)
    {
        return GetEventDescriptionPrefixBase(eventType);
    }

    /// <summary>
    /// Escapes special characters for Telegram MarkdownV2.
    /// </summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>The escaped text.</returns>
    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Telegram MarkdownV2 requires escaping these characters outside of code blocks
        var specialChars = new[] { '\\', '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
        
        foreach (var ch in specialChars)
        {
            text = text.Replace(ch.ToString(), $"\\{ch}", StringComparison.Ordinal);
        }

        return text;
    }
}
