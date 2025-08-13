using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;
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
    /// <returns>A read-only collection of tuples containing Discord embeds, image streams, and unique image names.</returns>
    public ReadOnlyCollection<(Embed Embed, MemoryStream? ResizedImageStream, string UniqueImageName)> BuildEmbedsFromNewsletterData(string serverId)
    {
        var completed = new HashSet<string>();
        var result = new List<(Embed, MemoryStream?, string)>();

        try
        {
            Db.CreateConnection();

            foreach (var row in Db.Query("SELECT * FROM CurrNewsletterData;"))
            {
                if (row is not null)
                {
                    JsonFileObj item = JsonFileObj.ConvertToObj(row);

                    if (!completed.Add(item.Title))
                    {
                        continue;
                    }

                    int embedColor = Convert.ToInt32(Config.DiscordMoviesEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16);
                    string seaEps = string.Empty;
                    if (item.Type == "Series")
                    {
                        // for series only
                        ReadOnlyCollection<NlDetailsJson> parsedInfoList = ParseSeriesInfo(item);
                        seaEps += GetSeasonEpisode(parsedInfoList);
                        embedColor = Convert.ToInt32(Config.DiscordSeriesEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16);
                    }

                    var fieldsList = new Collection<EmbedField>();

                    AddFieldIfEnabled(fieldsList, Config.DiscordRatingEnabled, "Rating", item.CommunityRating?.ToString(CultureInfo.InvariantCulture) ?? "N/A");
                    AddFieldIfEnabled(fieldsList, Config.DiscordPGRatingEnabled, "PG rating", item.OfficialRating ?? "N/A");
                    AddFieldIfEnabled(fieldsList, Config.DiscordDurationEnabled, "Duration", $"{item.RunTime} min");
                    AddFieldIfEnabled(fieldsList, Config.DiscordEpisodesEnabled, "Episodes", seaEps, false);

                    var embed = new Embed
                    {
                        Title = item.Title,
                        Url = $"{Config.Hostname}/web/index.html#/details?id={item.ItemID}&serverId={serverId}",
                        Color = embedColor,
                        Timestamp = DateTime.UtcNow.ToString("o"),
                        Fields = fieldsList.AsReadOnly(),
                    };

                    // Check if DiscordDescriptionEnabled is true
                    if (Config.DiscordDescriptionEnabled)
                    {
                        embed.Description = item.SeriesOverview;
                    }

                    MemoryStream? resizedImageStream = null;
                    string uniqueImageName = string.Empty;

                    // Check if DiscordThumbnailEnabled is true
                    if (Config.DiscordThumbnailEnabled)
                    {
                        if (Config.PosterType == "attachment")
                        {
                            (resizedImageStream, uniqueImageName, var success) = ResizeImage(item.PosterPath);

                            embed.Thumbnail = new Thumbnail
                            {
                                Url = $"attachment://{uniqueImageName}"
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
    /// <returns>A read-only collection containing a single test embed.</returns>
    public ReadOnlyCollection<Embed> BuildEmbedForTest()
    {
        Collection<Embed> embeds = new Collection<Embed>();

        try
        {
            // Populating embed with reference to a Series, as it'll will cover all the cases
            int embedColor = Convert.ToInt32(Config.DiscordSeriesEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16);
            string seaEps = "Season: 1 - Eps. 1 - 10\nSeason: 2 - Eps. 1 - 10\nSeason: 3 - Eps. 1 - 10";

            var fieldsList = new Collection<EmbedField>();

            AddFieldIfEnabled(fieldsList, Config.DiscordRatingEnabled, "Rating", "8.4");
            AddFieldIfEnabled(fieldsList, Config.DiscordPGRatingEnabled, "PG rating", "TV-14");
            AddFieldIfEnabled(fieldsList, Config.DiscordDurationEnabled, "Duration", "45 min");
            AddFieldIfEnabled(fieldsList, Config.DiscordEpisodesEnabled, "Episodes", seaEps, false);

            var embed = new Embed
            {
                Title = "Newsletter-Test",
                Url = Config.Hostname,
                Color = embedColor,
                Timestamp = DateTime.UtcNow.ToString("o"),
                Fields = fieldsList.AsReadOnly(),
            };

            // Check if DiscordDescriptionEnabled is true
            if (Config.DiscordDescriptionEnabled)
            {
                embed.Description = "Newsletter Lorem ipsum dolor sit amet, consectetur adipiscing elit. Vestibulum sit amet feugiat lectus. Mauris eu commodo arcu. Cras auctor ipsum nec sem vestibulum pellentesque.";
            }

            // Check if DiscordThumbnailEnabled is true
            if (Config.DiscordThumbnailEnabled)
            {
                embed.Thumbnail = new Thumbnail
                {
                    Url = "https://raw.githubusercontent.com/Sanidhya30/Jellyfin-Newsletter/refs/heads/master/logo.png"
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
        string seaEps = string.Empty;
        foreach (NlDetailsJson obj in list)
        {
            Logger.Debug("SNIPPET OBJ: " + JsonConvert.SerializeObject(obj));
            seaEps += "Season: " + obj.Season + " - Eps. " + obj.EpisodeRange + "\n";
        }

        return seaEps;
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
