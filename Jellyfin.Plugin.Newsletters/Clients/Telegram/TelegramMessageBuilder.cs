using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
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
    /// <returns>A formatted test message string.</returns>
    public string BuildTestMessage()
    {
        JsonFileObj item = JsonFileObj.GetTestObj();
        
        return BuildMessageText(item, "test");
    }

    /// <summary>
    /// Builds Telegram messages from newsletter data.
    /// </summary>
    /// <param name="systemId">The Jellyfin system ID.</param>
    /// <returns>A collection of message tuples containing text and optional image data.</returns>
    public ReadOnlyCollection<(string MessageText, string? ImageUrl, MemoryStream? ImageStream, string UniqueImageName)> BuildMessagesFromNewsletterData(string systemId)
    {
        var completed = new HashSet<string>();
        var result = new List<(string, string?, MemoryStream?, string)>();

        try
        {
            Db.CreateConnection();

            foreach (var row in Db.Query("SELECT * FROM CurrNewsletterData;"))
            {
                if (row is not null)
                {
                    JsonFileObj item = JsonFileObj.ConvertToObj(row);

                    // Event type filtering...
                    string eventType = item.EventType?.ToLowerInvariant() ?? "add";
                    if (eventType == "add" && !Config.NewsletterOnItemAddedEnabled) continue;
                    if (eventType == "update" && !Config.NewsletterOnItemUpdatedEnabled) continue;
                    if (eventType == "delete" && !Config.NewsletterOnItemDeletedEnabled) continue;

                    string uniqueKey = $"{item.Title}_{eventType}";
                    if (completed.Contains(uniqueKey)) continue;

                    var messageText = BuildMessageText(item, systemId);
                    
                    string? imageUrl = null;
                    MemoryStream? resizedImageStream = null;
                    string uniqueImageName = string.Empty;

                    if (Config.TelegramThumbnailEnabled)
                    {
                        if (Config.PosterType == "attachment")
                        {
                            // Upload as multipart file
                            (resizedImageStream, uniqueImageName, var success) = ResizeImage(item.PosterPath);
                        }
                        else
                        {
                            // Use external URL directly - Telegram will download it
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
    /// <returns>The formatted message text.</returns>
    private string BuildMessageText(JsonFileObj item, string systemId)
    {
        var messageBuilder = new System.Text.StringBuilder();

        // Get event type prefix
        string eventType = item.EventType?.ToLowerInvariant() ?? "add";
        string eventPrefix = GetEventDescriptionPrefix(eventType);
        
        // Add header with event type
        messageBuilder.AppendLine(EscapeMarkdown(eventPrefix));
        messageBuilder.AppendLine();
        
        // Add title
        messageBuilder.AppendLine(CultureInfo.InvariantCulture, $"*{EscapeMarkdown(item.Title)}*");
        messageBuilder.AppendLine();

        // Add series/episode information if available
        if (item.Type == "Series" && Config.TelegramEpisodesEnabled)
        {
            ReadOnlyCollection<NlDetailsJson> parsedInfoList = ParseSeriesInfo(item);
            string seaEps = GetSeasonEpisode(parsedInfoList);
            
            if (!string.IsNullOrWhiteSpace(seaEps))
            {
                messageBuilder.AppendLine("*Episodes:*");
                messageBuilder.AppendLine(EscapeMarkdown(seaEps.TrimEnd('\n')));
                messageBuilder.AppendLine();
            }
        }

        // Add description if enabled
        if (Config.TelegramDescriptionEnabled && !string.IsNullOrEmpty(item.SeriesOverview))
        {
            messageBuilder.AppendLine(EscapeMarkdown(item.SeriesOverview));
            messageBuilder.AppendLine();
        }

        // Add metadata fields
        var metadataParts = new List<string>();

        if (Config.TelegramRatingEnabled && item.CommunityRating.HasValue)
        {
            var ratingString = item.CommunityRating.Value.ToString($"F{Config.CommunityRatingDecimalPlaces}", CultureInfo.InvariantCulture);
            metadataParts.Add($"Rating: {EscapeMarkdown(ratingString)}");
        }

        if (Config.TelegramPGRatingEnabled && !string.IsNullOrEmpty(item.OfficialRating))
        {
            metadataParts.Add($"PG Rating: {EscapeMarkdown(item.OfficialRating)}");
        }

        if (Config.TelegramDurationEnabled && item.RunTime > 0)
        {
            metadataParts.Add($"Duration: {item.RunTime} min");
        }

        if (metadataParts.Count > 0)
        {
            foreach (var part in metadataParts)
            {
                messageBuilder.AppendLine(part);
            }

            messageBuilder.AppendLine();
        }

        // Add Jellyfin link if hostname is configured
        if (!string.IsNullOrEmpty(Config.Hostname) && !string.IsNullOrEmpty(item.ItemID))
        {
            string jellyfinUrl = $"{Config.Hostname}/web/index.html#/details?id={item.ItemID}&serverId={systemId}&event={eventType}";
            messageBuilder.AppendLine(CultureInfo.InvariantCulture, $"[View in Jellyfin]({jellyfinUrl})");
        }

        return messageBuilder.ToString().Trim();
    }

    private string GetSeasonEpisode(IReadOnlyCollection<NlDetailsJson> list)
    {
        var seaEps = new System.Text.StringBuilder();
        foreach (NlDetailsJson obj in list)
        {
            seaEps.AppendLine(CultureInfo.InvariantCulture, $"Season: {obj.Season} - Eps. {obj.EpisodeRange}");
        }

        return seaEps.ToString();
    }

    /// <summary>
    /// Gets the description prefix for the message based on the event type.
    /// </summary>
    /// <param name="eventType">The event type (add, delete, update).</param>
    /// <returns>The formatted description prefix with emoji.</returns>
    private static string GetEventDescriptionPrefix(string? eventType)
    {
        if (string.IsNullOrEmpty(eventType))
        {
            return "🎬 Added to Library";
        }

        return eventType.ToLowerInvariant() switch
        {
            "add" => "🎬 Added to Library",
            "delete" => "🗑️ Removed from Library",
            "update" => "🔄 Updated in Library",
            _ => "🎬 Added to Library"
        };
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
        var specialChars = new[] { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
        
        foreach (var ch in specialChars)
        {
            text = text.Replace(ch.ToString(), $"\\{ch}", StringComparison.Ordinal);
        }
        
        // Also escape backslash
        text = text.Replace("\\\\", "\\\\\\\\", StringComparison.Ordinal);

        return text;
    }
}
