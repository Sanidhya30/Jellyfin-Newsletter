using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Integrations;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Newsletters.Clients.Discord;

/// <summary>
/// Provides functionality to build Discord embeds from newsletter data for sending via Discord webhooks.
/// </summary>
/// <param name="loggerInstance">The logger instance for logging operations.</param>
/// <param name="dbInstance">The database instance for data access.</param>
/// <param name="libraryManager">The library manager for resolving library names.</param>
/// <param name="upcomingItems">The list of prefetched upcoming media items.</param>
public class EmbedBuilder(
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManager,
    IReadOnlyList<JsonFileObj> upcomingItems)
    : ClientBuilder(loggerInstance, dbInstance, libraryManager)
{
     /// <summary>
    /// Builds Discord embeds from newsletter data stored in the database.
    /// Groups entries by event type (Add, Update, Delete), then by library name (Movies first, then Series).
    /// </summary>
    /// <param name="serverId">The Jellyfin server ID to include in embed URLs.</param>
    /// <param name="discordConfig">The Discord configuration to use for building embeds.</param>
    /// <returns>A read-only collection of tuples containing Discord embeds, image streams, and unique image names.</returns>
    public ReadOnlyCollection<(Embed Embed, MemoryStream? ResizedImageStream, string UniqueImageName)> BuildEmbedsFromNewsletterData(string serverId, DiscordConfiguration discordConfig)
    {
        var result = new List<(Embed, MemoryStream?, string)>();

        // Build library name map
        var libraryNameMap = BuildLibraryNameMap();

        // BuildSortedItems manages its own DB connection for querying/deduplication
        var sortedItems = BuildSortedItems(discordConfig, upcomingItems, "Discord");

        try
        {
            // Open connection for ParseSeriesInfo calls inside the item loop
            Db.CreateConnection();

            // Build embeds from sorted items
            foreach (var item in sortedItems)
            {
                string eventType = item.EventType?.ToLowerInvariant() ?? "add";
                string libraryName = eventType == "upcoming" ? (item.LibraryId ?? string.Empty) : GetLibraryName(item.LibraryId, libraryNameMap);

                int embedColor = GetEventColor(item.EventType, item.Type, discordConfig);
                var fieldsList = new Collection<EmbedField>();

                string seaEps = string.Empty;
                if (item.Type == "Series")
                {
                    // for series only
                    ReadOnlyCollection<NlDetailsJson> parsedInfoList = ParseSeriesInfo(item, upcomingItems);
                    seaEps += GetSeasonEpisode(parsedInfoList);
                }
                
                if (eventType == "upcoming")
                {
                    AddFieldIfEnabled(fieldsList, true, "Release Date", item.PremiereYear ?? "N/A");
                }
                
                AddFieldIfEnabled(fieldsList, discordConfig.RatingEnabled, "Rating", item.CommunityRating > 0 ? item.CommunityRating.Value.ToString($"F{Config.CommunityRatingDecimalPlaces}", CultureInfo.InvariantCulture) : "N/A");
                AddFieldIfEnabled(fieldsList, discordConfig.PGRatingEnabled, "PG rating", item.OfficialRating ?? "N/A");
                AddFieldIfEnabled(fieldsList, discordConfig.DurationEnabled, "Duration", item.RunTime > 0 ? $"{item.RunTime} min" : "N/A");
                AddFieldIfEnabled(fieldsList, discordConfig.EpisodesEnabled, "Episodes", seaEps, false);

                // Add event type query otherwise discord deduplicate the embed with same url
                // For eg. an item of the same series got added and another got deleted, both will have same url without the event type query
                // Adding event type query should not cause issue
                string embedUrl = string.IsNullOrEmpty(Config.Hostname) || eventType == "upcoming" || eventType == "delete"
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
                    embed.Description = GetEventDescriptionPrefix(item.EventType, libraryName) + "\n" + item.SeriesOverview;
                }
                else
                {
                    // If description is disabled, still show the event type prefix
                    embed.Description = GetEventDescriptionPrefix(item.EventType, libraryName);
                }

                MemoryStream? resizedImageStream = null;
                string uniqueImageName = string.Empty;

                // Check if DiscordThumbnailEnabled is true
                if (discordConfig.ThumbnailEnabled)
                {
                    if (Config.PosterType == "attachment")
                    {
                        if (item.EventType == "upcoming")
                        {
                            embed.Thumbnail = new Thumbnail { Url = item.ImageURL };
                        }
                        else
                        {
                            (resizedImageStream, uniqueImageName, var success) = ResizeImage(item.PosterPath);

                            string thumbnailUrl = success ? $"attachment://{uniqueImageName}" : item.ImageURL;
                            embed.Thumbnail = new Thumbnail
                            {
                                Url = thumbnailUrl
                            };
                        }
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

        if (eventLower == "upcoming")
        {
            return Convert.ToInt32("FF8C00", 16); // Orange for upcoming items
        }
        
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
    /// Gets the description prefix for the embed based on the event type and library name.
    /// </summary>
    /// <param name="eventType">The event type (Add, Delete, Update).</param>
    /// <param name="libraryName">Optional library name to include in the prefix.</param>
    /// <returns>The formatted description prefix with emoji and bold formatting.</returns>
    private string GetEventDescriptionPrefix(string? eventType, string? libraryName = null)
    {
        string basePrefix = GetEventDescriptionPrefixBase(eventType, libraryName);
        string libDisplay = string.IsNullOrEmpty(libraryName) ? "Library" : libraryName;

        // Discord uses bold formatting for the description prefix
        return basePrefix.Replace($"Added to {libDisplay}", $"**Added to {libDisplay}**", StringComparison.Ordinal)
                        .Replace($"Removed from {libDisplay}", $"**Removed from {libDisplay}**", StringComparison.Ordinal)
                        .Replace($"Updated in {libDisplay}", $"**Updated in {libDisplay}**", StringComparison.Ordinal)
                        .Replace($"Upcoming in {libDisplay}", $"**Upcoming in {libDisplay}**", StringComparison.Ordinal);
    }
}
