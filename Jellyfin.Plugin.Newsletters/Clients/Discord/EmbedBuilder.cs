#pragma warning disable 1591, SYSLIB0014, CA1002, CA2227, CS0162, SA1005, SA1300 // remove SA1005 for cleanup
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller.Library;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Newsletters.Clients.Discord;

public class EmbedBuilder(Logger loggerInstance,
    SQLiteDatabase dbInstance)
    : ClientBuilder(loggerInstance, dbInstance)
{
    public List<(Embed Embed, MemoryStream? ResizedImageStream, string UniqueImageName)> BuildEmbedsFromNewsletterData(string serverId)
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
                    JsonFileObj item = JsonHelper.ConvertToObj(row);

                    if (!completed.Add(item.Title))
                    {
                        continue;
                    }

                    int embedColor = Convert.ToInt32(Config.DiscordMoviesEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16);
                    string seaEps = string.Empty;
                    if (item.Type == "Series")
                    {
                        // for series only
                        List<NlDetailsJson> parsedInfoList = ParseSeriesInfo(item);
                        seaEps += GetSeasonEpisode(parsedInfoList);
                        embedColor = Convert.ToInt32(Config.DiscordSeriesEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16);
                    }

                    // string communityRating = item.CommunityRating.HasValue ? item.CommunityRating.Value.ToString(CultureInfo.InvariantCulture) : "N/A";

                    var fieldsList = new List<EmbedField>();

                    AddFieldIfEnabled(fieldsList, Config.DiscordRatingEnabled, "Rating", item.CommunityRating?.ToString(CultureInfo.InvariantCulture) ?? "N/A");
                    AddFieldIfEnabled(fieldsList, Config.DiscordPGRatingEnabled, "PG rating", item.OfficialRating ?? "N/A");
                    AddFieldIfEnabled(fieldsList, Config.DiscordDurationEnabled, "Duration", $"{item.RunTime} min");
                    AddFieldIfEnabled(fieldsList, Config.DiscordEpisodesEnabled, "Episodes", seaEps, false);

                    var embed = new Embed
                    {
                        title = item.Title,
                        url = $"{Config.Hostname}/web/index.html#/details?id={item.ItemID}&serverId={serverId}",
                        color = embedColor,
                        timestamp = DateTime.UtcNow.ToString("o"),
                        fields = fieldsList,
                    };

                    // Check if DiscordDescriptionEnabled is true
                    if (Config.DiscordDescriptionEnabled)
                    {
                        embed.description = item.SeriesOverview;
                    }

                    MemoryStream? resizedImageStream = null;
                    string uniqueImageName = string.Empty;

                    // Check if DiscordThumbnailEnabled is true
                    if (Config.DiscordThumbnailEnabled)
                    {
                        if (Config.PosterType == "attachment")
                        {
                            (resizedImageStream, uniqueImageName, var success) = ResizeImage(item.PosterPath);

                            embed.thumbnail = new Thumbnail
                            {
                                url = $"attachment://{uniqueImageName}"
                            };
                        }
                        else 
                        {
                            // If PosterType is not "attachment", use the image URL
                            embed.thumbnail = new Thumbnail
                            {
                                url = item.ImageURL
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

        return result;
    }

    public List<Embed> BuildEmbedForTest()
    {
        List<Embed> embeds = new List<Embed>();

        try
        {
            // Populating embed with reference to a Series, as it'll will cover all the cases
            int embedColor = Convert.ToInt32(Config.DiscordSeriesEmbedColor.Replace("#", string.Empty, StringComparison.Ordinal), 16);
            string seaEps = "Season: 1 - Eps. 1 - 10\nSeason: 2 - Eps. 1 - 10\nSeason: 3 - Eps. 1 - 10";

            var fieldsList = new List<EmbedField>();

            AddFieldIfEnabled(fieldsList, Config.DiscordRatingEnabled, "Rating", "8.4");
            AddFieldIfEnabled(fieldsList, Config.DiscordPGRatingEnabled, "PG rating", "TV-14");
            AddFieldIfEnabled(fieldsList, Config.DiscordDurationEnabled, "Duration", "45 min");
            AddFieldIfEnabled(fieldsList, Config.DiscordEpisodesEnabled, "Episodes", seaEps, false);

            var embed = new Embed
            {
                title = "Newsletter-Test",
                url = Config.Hostname,
                color = embedColor,
                timestamp = DateTime.UtcNow.ToString("o"),
                fields = fieldsList,
            };

            // Check if DiscordDescriptionEnabled is true
            if (Config.DiscordDescriptionEnabled)
            {
                embed.description = "Newsletter Lorem ipsum dolor sit amet, consectetur adipiscing elit. Vestibulum sit amet feugiat lectus. Mauris eu commodo arcu. Cras auctor ipsum nec sem vestibulum pellentesque.";
            }

            // Check if DiscordThumbnailEnabled is true
            if (Config.DiscordThumbnailEnabled)
            {
                embed.thumbnail = new Thumbnail
                {
                    url = "https://raw.githubusercontent.com/Sanidhya30/Jellyfin-Newsletter/refs/heads/master/logo.png"
                };
            }

            embeds.Add(embed);
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }

        return embeds;
    }

    private string GetSeasonEpisode(List<NlDetailsJson> list)
    {
        string seaEps = string.Empty;
        foreach (NlDetailsJson obj in list)
        {
            Logger.Debug("SNIPPET OBJ: " + JsonConvert.SerializeObject(obj));
            seaEps += "Season: " + obj.Season + " - Eps. " + obj.EpisodeRange + "\n";
        }

        return seaEps;
    }

    private static void AddFieldIfEnabled(List<EmbedField> fieldsList, bool isEnabled, string name, string value, bool inline = true)
    {
        if (isEnabled && !string.IsNullOrWhiteSpace(value))
        {
            fieldsList.Add(new EmbedField
            {
                name = name,
                value = value,
                inline = inline
            });
        }
    }
}

public class EmbedField
{
    public string? name { get; set; }

    public string? value { get; set; }

    public bool inline { get; set; }
}

public class Embed
{
    public string? title { get; set; }

    public string? url { get; set; }

    public int color { get; set; }

    public string? timestamp { get; set; }

    public string? description { get; set; }

    public List<EmbedField>? fields { get; set; }

    public Thumbnail? thumbnail { get; set; }
}

public class DiscordPayload
{
    public string? username { get; set; }

    public List<Embed>? embeds { get; set; }
}

public class Thumbnail
{
    public string? url { get; set; }
}
