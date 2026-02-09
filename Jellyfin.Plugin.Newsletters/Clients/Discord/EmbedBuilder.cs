using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Newsletters.Clients.Discord;

/// <summary>
/// Provides functionality to build Discord embeds from newsletter data for sending via Discord webhooks.
/// </summary>
/// <param name="loggerInstance">The logger instance for logging operations.</param>
/// <param name="dbInstance">The database instance for data access.</param>
public class EmbedBuilder(Logger loggerInstance,
    SQLiteDatabase dbInstance)
    : ClientBuilder(loggerInstance, dbInstance)
{
     /// <summary>
    /// Builds Discord embeds from newsletter data stored in the database.
    /// </summary>
    /// <param name="serverId">The Jellyfin server ID to include in embed URLs.</param>
    /// <param name="discordConfig">The Discord configuration to use for building embeds.</param>
    /// <returns>A read-only collection of tuples containing Discord embeds, image streams, and unique image names.</returns>
    public ReadOnlyCollection<(Embed Embed, MemoryStream? ResizedImageStream, string UniqueImageName)> BuildEmbedsFromNewsletterData(string serverId, DiscordConfiguration discordConfig)
    {
        var completed = new HashSet<string>(); // Store "Title_EventType"
        var result = new List<(Embed, MemoryStream?, string)>();

        try
        {
            Db.CreateConnection();

            foreach (var row in Db.Query("SELECT * FROM CurrNewsletterData;"))
            {
                if (row is not null)
                {
                    JsonFileObj item = JsonFileObj.ConvertToObj(row);

                    // Check if the event type should be included based on configuration
                    string eventType = item.EventType?.ToLowerInvariant() ?? "add";
                    if (eventType == "add" && !Config.NewsletterOnItemAddedEnabled)
                    {
                        continue;
                    }
                    else if (eventType == "update" && !Config.NewsletterOnItemUpdatedEnabled)
                    {
                        continue;
                    }
                    else if (eventType == "delete" && !Config.NewsletterOnItemDeletedEnabled)
                    {
                        continue;
                    }

                    // Create a unique key combining title and event type
                    string uniqueKey = $"{item.Title}_{eventType}";
                    if (completed.Contains(uniqueKey))
                    {
                        continue;
                    }

                    int embedColor = GetEventColor(item.EventType, item.Type, discordConfig);
                    string seaEps = string.Empty;
                    if (item.Type == "Series")
                    {
                        // for series only
                        ReadOnlyCollection<NlDetailsJson> parsedInfoList = ParseSeriesInfo(item);
                        seaEps += GetSeasonEpisode(parsedInfoList);
                    }

                    var fieldsList = new Collection<EmbedField>();

                    AddFieldIfEnabled(fieldsList, discordConfig.RatingEnabled, "Rating", item.CommunityRating?.ToString($"F{Config.CommunityRatingDecimalPlaces}", CultureInfo.InvariantCulture) ?? "N/A");
                    AddFieldIfEnabled(fieldsList, discordConfig.PGRatingEnabled, "PG rating", item.OfficialRating ?? "N/A");
                    AddFieldIfEnabled(fieldsList, discordConfig.DurationEnabled, "Duration", $"{item.RunTime} min");
                    AddFieldIfEnabled(fieldsList, discordConfig.EpisodesEnabled, "Episodes", seaEps, false);

                    // Add event type query otherwise discord deduplicate the embed with same url
                    // For eg. an item of the same series got added and another got deleted, both will have same url without the event type query
                    // Adding event type query should not cause issue
                    string embedUrl = string.IsNullOrEmpty(Config.Hostname) 
                        ? string.Empty 
                        : $"{Config.Hostname}/web/index.html#/details?id={item.ItemID}&serverId={serverId}&event={eventType}";
                    var embed = new Embed
                    {
                        Title = item.Title,
                        Url = embedUrl,
                        Color = embedColor,
                        Timestamp = DateTime.UtcNow.ToString("o"),
                        Fields = fieldsList.AsReadOnly(),
                    };

                    // Check if DiscordDescriptionEnabled is true
                    if (discordConfig.DescriptionEnabled)
                    {
                        embed.Description = GetEventDescriptionPrefix(item.EventType) + "\n" + item.SeriesOverview;
                    }
                    else
                    {
                        // If description is disabled, still show the event type prefix
                        embed.Description = GetEventDescriptionPrefix(item.EventType);
                    }

                    MemoryStream? resizedImageStream = null;
                    string uniqueImageName = string.Empty;

                    // Check if DiscordThumbnailEnabled is true
                    if (discordConfig.ThumbnailEnabled)
                    {
                        if (Config.PosterType == "attachment")
                        {
                            (resizedImageStream, uniqueImageName, var success) = ResizeImage(item.PosterPath);

                            string thumbnailUrl = success ? $"attachment://{uniqueImageName}" : item.ImageURL;
                            embed.Thumbnail = new Thumbnail
                            {
                                Url = thumbnailUrl
                            };
                        }
                        else 
                        {
                            // If PosterType is not "attachment", use the image URL
                            embed.Thumbnail = new Thumbnail
                            {
                                Url = item.ImageURL
                            };
                        }
                    }

                    result.Add((embed, resizedImageStream, uniqueImageName));
                    completed.Add(uniqueKey);
                }
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

        return result.AsReadOnly();
    }

    /// <summary>
    /// Builds a test Discord embed with sample data for testing webhook connectivity.
    /// </summary>
    /// <param name="discordConfig">The Discord configuration to use for building the test embed.</param>
    /// <returns>A read-only collection containing a single test embed.</returns>
    public ReadOnlyCollection<Embed> BuildEmbedForTest(DiscordConfiguration discordConfig)
    {
        Collection<Embed> embeds = new Collection<Embed>();

        try
        {
            // Use test object for consistency with HTML test
            JsonFileObj item = JsonFileObj.GetTestObj();

            // Populating embed with reference to a Series, as it'll will cover all the cases
            int embedColor = GetEventColor("add", "Series", discordConfig);
            string seaEps = "Season: 1 - Eps. 1 - 10\nSeason: 2 - Eps. 1 - 10\nSeason: 3 - Eps. 1 - 10";

            var fieldsList = new Collection<EmbedField>();

            AddFieldIfEnabled(fieldsList, discordConfig.RatingEnabled, "Rating", item.CommunityRating?.ToString($"F{Config.CommunityRatingDecimalPlaces}", CultureInfo.InvariantCulture) ?? "N/A");
            AddFieldIfEnabled(fieldsList, discordConfig.PGRatingEnabled, "PG rating", item.OfficialRating ?? "N/A");
            AddFieldIfEnabled(fieldsList, discordConfig.DurationEnabled, "Duration", $"{item.RunTime} min");
            AddFieldIfEnabled(fieldsList, discordConfig.EpisodesEnabled, "Episodes", seaEps, false);

            string embedUrl = string.IsNullOrEmpty(Config.Hostname) 
                ? string.Empty
                : Config.Hostname;
            var embed = new Embed
            {
                Title = item.Title,
                Url = embedUrl,
                Color = embedColor,
                Timestamp = DateTime.UtcNow.ToString("o"),
                Fields = fieldsList.AsReadOnly(),
            };

            // Check if DiscordDescriptionEnabled is true
            if (discordConfig.DescriptionEnabled)
            {
                embed.Description = GetEventDescriptionPrefix("add") + "\n" + item.SeriesOverview;
            }
            else
            {
                embed.Description = GetEventDescriptionPrefix("add");
            }

            // Check if DiscordThumbnailEnabled is true
            if (discordConfig.ThumbnailEnabled)
            {
                embed.Thumbnail = new Thumbnail
                {
                    Url = item.ImageURL
                };
            }

            embeds.Add(embed);
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }

        return embeds.AsReadOnly();
    }

    private string GetSeasonEpisode(IReadOnlyCollection<NlDetailsJson> list)
    {
        return GetSeasonEpisodeBase(list);
    }

    private static void AddFieldIfEnabled(Collection<EmbedField> fieldsList, bool isEnabled, string name, string value, bool inline = true)
    {
        if (isEnabled && !string.IsNullOrWhiteSpace(value))
        {
            fieldsList.Add(new EmbedField
            {
                Name = name,
                Value = value,
                Inline = inline
            });
        }
    }

    /// <summary>
    /// Gets the embed color based on the event type and media type.
    /// </summary>
    /// <param name="eventType">The event type (Add, Delete, Update).</param>
    /// <param name="mediaType">The media type (Series or Movie).</param>
    /// <param name="discordConfig">The Discord configuration containing color settings.</param>
    /// <returns>The color as an integer value.</returns>
    private static int GetEventColor(string? eventType, string mediaType, DiscordConfiguration discordConfig)
    {
        if (string.IsNullOrEmpty(eventType))
        {
            // Default to add event colors if event type is not recognized
            return mediaType == "Series" 
                ? Convert.ToInt32(discordConfig.SeriesAddEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16)
                : Convert.ToInt32(discordConfig.MoviesAddEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16);
        }

        var eventLower = eventType.ToLowerInvariant();
        
        if (mediaType == "Series")
        {
            return eventLower switch
            {
                "add" => Convert.ToInt32(discordConfig.SeriesAddEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16),
                "delete" => Convert.ToInt32(discordConfig.SeriesDeleteEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16),
                "update" => Convert.ToInt32(discordConfig.SeriesUpdateEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16),
                _ => Convert.ToInt32(discordConfig.SeriesAddEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16)
            };
        }
        else
        {
            return eventLower switch
            {
                "add" => Convert.ToInt32(discordConfig.MoviesAddEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16),
                "delete" => Convert.ToInt32(discordConfig.MoviesDeleteEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16),
                "update" => Convert.ToInt32(discordConfig.MoviesUpdateEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16),
                _ => Convert.ToInt32(discordConfig.MoviesAddEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16)
            };
        }
    }

    /// <summary>
    /// Gets the description prefix for the embed based on the event type.
    /// </summary>
    /// <param name="eventType">The event type (Add, Delete, Update).</param>
    /// <returns>The formatted description prefix with emoji.</returns>
    private string GetEventDescriptionPrefix(string? eventType)
    {
        string basePrefix = GetEventDescriptionPrefixBase(eventType);
        // Discord uses bold formatting for the description prefix
        return basePrefix.Replace("Added to Library", "**Added to Library**", StringComparison.Ordinal)
                        .Replace("Removed from Library", "**Removed from Library**", StringComparison.Ordinal)
                        .Replace("Updated in Library", "**Updated in Library**", StringComparison.Ordinal);
    }
}

/// <summary>
/// Represents a field within a Discord embed with name, value, and inline properties.
/// </summary>
public class EmbedField
{
    /// <summary>
    /// Gets or sets the name of the embed field.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the value of the embed field.
    /// </summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field should be displayed inline.
    /// </summary>
    [JsonPropertyName("inline")]
    public bool Inline { get; set; }
}

/// <summary>
/// Represents a Discord embed message with title, description, fields, and other formatting options.
/// </summary>
public class Embed
{
    /// <summary>
    /// Gets or sets the title of the embed.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the URL that the embed title links to.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the color of the embed as an integer value.
    /// </summary>
    [JsonPropertyName("color")]
    public int Color { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the embed in ISO 8601 format.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the description text of the embed.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the collection of fields to display in the embed.
    /// </summary>
    [JsonPropertyName("fields")]
    public ReadOnlyCollection<EmbedField>? Fields { get; set; }

    /// <summary>
    /// Gets or sets the thumbnail image for the embed.
    /// </summary>
    [JsonPropertyName("thumbnail")]
    public Thumbnail? Thumbnail { get; set; }
}

/// <summary>
/// Represents the payload structure for sending messages to Discord webhooks.
/// </summary>
public class DiscordPayload
{
    /// <summary>
    /// Gets or sets the username to display for the webhook message.
    /// </summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the collection of embeds to include in the webhook message.
    /// </summary>
    [JsonPropertyName("embeds")]
    public ReadOnlyCollection<Embed>? Embeds { get; set; }
}

/// <summary>
/// Represents a thumbnail image for a Discord embed.
/// </summary>
public class Thumbnail
{
    /// <summary>
    /// Gets or sets the URL of the thumbnail image.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
