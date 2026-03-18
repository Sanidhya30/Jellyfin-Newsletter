using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Newsletters.Clients.Matrix;

/// <summary>
/// Builds HTML-formatted Matrix messages from newsletter data.
/// </summary>
public class MatrixMessageBuilder(
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManager,
    IReadOnlyList<JsonFileObj> upcomingItems)
    : ClientBuilder(loggerInstance, dbInstance, libraryManager)
{
    /// <summary>
    /// Test poster image URLs from TMDB used for rich test messages.
    /// </summary>
    public static readonly IReadOnlyList<(string Title, string Url)> TestPosterUrls = new List<(string, string)>
    {
        ("Dune_Part_Two", "https://placehold.co/200x300/1a1a2e/e94560.jpg?text=Dune:%0APart+Two"),
        ("Shogun", "https://placehold.co/200x300/2d132c/d4a373.jpg?text=Shogun"),
        ("The_Bear", "https://placehold.co/200x300/0d1b2a/7ec8e3.jpg?text=The+Bear"),
    }.AsReadOnly();

    /// <summary>
    /// Builds a rich test message. Poster image placeholders use {{POSTER:index}} tokens
    /// that the caller replaces with mxc:// URLs after uploading.
    /// </summary>
    /// <param name="config">The Matrix configuration.</param>
    /// <returns>Tuple of (htmlBody, plainTextBody).</returns>
    public (string Html, string PlainText) BuildTestMessage(MatrixConfiguration config)
    {
        var html = new StringBuilder();
        var plain = new StringBuilder();

        html.Append("<h2>🎬 Jellyfin Newsletter</h2>");
        plain.AppendLine("Jellyfin Newsletter");
        plain.AppendLine("===");

        // Movie item
        html.Append("<hr>");
        if (config.ThumbnailEnabled)
        {
            html.Append("<table><tr><td valign=\"top\">{{POSTER:0}}</td><td valign=\"top\">");
        }

        html.Append("<b><a href=\"https://jellyfin.example.com/web/index.html#/details?id=test1\">Dune: Part Two</a></b><br>");
        html.Append("2024 • 166 min • PG-13 • ⭐ 8.3<br>");
        html.Append("🎬 <b>Added to Movies</b>");
        if (config.DescriptionEnabled)
        {
            html.Append("<br><br><i>Paul Atreides unites with the Fremen while on a warpath of revenge against the conspirators who destroyed his family.</i>");
        }

        if (config.ThumbnailEnabled)
        {
            html.Append("</td></tr></table>");
        }

        plain.AppendLine("---");
        plain.AppendLine("Dune: Part Two (2024)");
        plain.AppendLine("Added to Movies · 8.3 · PG-13 · 166 min");

        // Series item with episodes
        html.Append("<hr>");
        if (config.ThumbnailEnabled)
        {
            html.Append("<table><tr><td valign=\"top\">{{POSTER:1}}</td><td valign=\"top\">");
        }

        html.Append("<b><a href=\"https://jellyfin.example.com/web/index.html#/details?id=test2\">Shogun</a></b><br>");
        html.Append("2024 • TV-MA • ⭐ 8.7<br>");
        html.Append("🎬 <b>Added to TV Shows</b><br>");
        if (config.EpisodesEnabled)
        {
            html.Append("📺 Season: 1 - Eps. 1 - 10");
        }

        if (config.DescriptionEnabled)
        {
            html.Append("<br><br><i>When a mysterious European ship is found marooned in a nearby fishing village, Lord Yoshii Toranaga discovers secrets that could tip the balance of power and devastate his enemies.</i>");
        }

        if (config.ThumbnailEnabled)
        {
            html.Append("</td></tr></table>");
        }

        plain.AppendLine("---");
        plain.AppendLine("Shogun (2024)");
        plain.AppendLine("Added to TV Shows · 8.7 · TV-MA");
        plain.AppendLine("Episodes: Season 1 - Eps. 1 - 10");

        // Recently updated item
        html.Append("<hr>");
        if (config.ThumbnailEnabled)
        {
            html.Append("<table><tr><td valign=\"top\">{{POSTER:2}}</td><td valign=\"top\">");
        }

        html.Append("<b><a href=\"https://jellyfin.example.com/web/index.html#/details?id=test3\">The Bear</a></b><br>");
        html.Append("2022 • TV-MA • ⭐ 8.6<br>");
        html.Append("🔄 <b>Updated in TV Shows</b><br>");
        if (config.EpisodesEnabled)
        {
            html.Append("📺 Season: 3 - Eps. 1 - 10");
        }

        if (config.DescriptionEnabled)
        {
            html.Append("<br><br><i>A young chef from the fine dining world returns to Chicago to run his family's sandwich shop.</i>");
        }

        if (config.ThumbnailEnabled)
        {
            html.Append("</td></tr></table>");
        }

        plain.AppendLine("---");
        plain.AppendLine("The Bear (2022)");
        plain.AppendLine("Updated in TV Shows · 8.6 · TV-MA");
        plain.AppendLine("Episodes: Season 3 - Eps. 1 - 10");

        html.Append("<hr>");
        html.Append("<p>🍿 <i>Sent from Jellyfin</i></p>");
        plain.AppendLine("---");
        plain.AppendLine("Sent from Jellyfin");

        return (html.ToString(), plain.ToString());
    }

    /// <summary>
    /// Builds messages from current newsletter data.
    /// </summary>
    /// <param name="systemId">The Jellyfin system ID for building URLs.</param>
    /// <param name="config">The Matrix configuration.</param>
    /// <returns>Collection of (htmlBody, plainTextBody, imagePath, uniqueImageName) tuples.</returns>
    public ReadOnlyCollection<(string Html, string PlainText, string? ImagePath, string UniqueImageName)>
        BuildMessagesFromNewsletterData(string systemId, MatrixConfiguration config)
    {
        var messages = new List<(string Html, string PlainText, string? ImagePath, string UniqueImageName)>();
        var libraryNameMap = BuildLibraryNameMap();

        try
        {
            Db.CreateConnection();

            var items = new Dictionary<string, JsonFileObj>();

            foreach (var row in Db.Query("SELECT * FROM CurrNewsletterData;"))
            {
                if (row is null)
                {
                    continue;
                }

                var item = JsonFileObj.ConvertToObj(row);
                if (!ShouldIncludeItem(item, config, "Matrix"))
                {
                    continue;
                }

                var key = $"{item.Title}_{item.EventType}";
                items.TryAdd(key, item);
            }

            // Add upcoming items
            if (upcomingItems != null && upcomingItems.Count > 0)
            {
                foreach (var item in upcomingItems)
                {
                    if (!ShouldIncludeItem(item, config, "Matrix"))
                    {
                        continue;
                    }

                    var key = $"{item.Title}_Upcoming";
                    items.TryAdd(key, item);
                }
            }

            // Sort: event type order → media type (movies first) → library name
            var eventTypeOrder = new Dictionary<string, int>
            {
                { "add", 0 }, { "update", 1 }, { "delete", 2 }, { "upcoming", 3 },
            };

            var sortedItems = items.Values
                .OrderBy(i => eventTypeOrder.GetValueOrDefault(i.EventType?.ToLowerInvariant() ?? "add", 0))
                .ThenBy(i => i.Type == "Movie" ? 0 : 1)
                .ThenBy(i => GetLibraryName(i.LibraryId, libraryNameMap))
                .ToList();

            foreach (var item in sortedItems)
            {
                string eventType = item.EventType?.ToLowerInvariant() ?? "add";
                string libraryName = eventType == "upcoming"
                    ? (item.LibraryId ?? string.Empty)
                    : GetLibraryName(item.LibraryId, libraryNameMap);

                var (html, plain) = BuildItemMessage(item, systemId, config, libraryName);
                var imagePath = config.ThumbnailEnabled ? item.PosterPath : null;
                var uniqueName = $"{item.Title}_{item.EventType}".Replace(" ", "_", StringComparison.Ordinal);
                messages.Add((html, plain, imagePath, uniqueName));
            }
        }
        catch (Exception e)
        {
            Logger.Error("Error building Matrix messages: " + e);
        }
        finally
        {
            Db.CloseConnection();
        }

        return messages.AsReadOnly();
    }

    private (string Html, string PlainText) BuildItemMessage(
        JsonFileObj item,
        string systemId,
        MatrixConfiguration config,
        string libraryName)
    {
        var html = new StringBuilder();
        var plain = new StringBuilder();

        var eventType = item.EventType?.ToLowerInvariant() ?? "add";
        var eventPrefix = GetEventDescriptionPrefixBase(eventType, libraryName);

        var title = item.Title ?? "Unknown Title";
        var year = item.PremiereYear ?? string.Empty;
        var titleWithYear = string.IsNullOrEmpty(year) ? title : $"{title} ({year})";

        // Build Jellyfin link
        var jellyfinUrl = string.Empty;
        if (!string.IsNullOrEmpty(Config.Hostname) && !string.IsNullOrEmpty(item.ItemID) && eventType != "upcoming")
        {
            jellyfinUrl = $"{Config.Hostname}/web/index.html#/details?id={item.ItemID}&serverId={systemId}&event={eventType}";
        }

        // Title (bold, with optional link)
        if (!string.IsNullOrEmpty(jellyfinUrl))
        {
            html.Append(CultureInfo.InvariantCulture, $"<b><a href=\"{jellyfinUrl}\">{title}</a></b><br>");
        }
        else
        {
            html.Append(CultureInfo.InvariantCulture, $"<b>{title}</b><br>");
        }

        // Metadata line: year • duration • content rating • ⭐ rating
        var metaParts = new List<string>();
        var plainMetaParts = new List<string>();

        if (!string.IsNullOrEmpty(year))
        {
            metaParts.Add(year);
            plainMetaParts.Add(year);
        }

        if (config.DurationEnabled && item.RunTime > 0)
        {
            metaParts.Add($"{item.RunTime} min");
            plainMetaParts.Add($"{item.RunTime} min");
        }

        if (config.PGRatingEnabled && !string.IsNullOrEmpty(item.OfficialRating))
        {
            metaParts.Add(item.OfficialRating);
            plainMetaParts.Add(item.OfficialRating);
        }

        if (config.RatingEnabled)
        {
            var ratingText = item.CommunityRating > 0
                ? item.CommunityRating.Value.ToString($"F{Config.CommunityRatingDecimalPlaces}", CultureInfo.InvariantCulture)
                : null;
            if (ratingText != null)
            {
                metaParts.Add($"⭐ {ratingText}");
                plainMetaParts.Add($"{ratingText} ★");
            }
        }

        if (metaParts.Count > 0)
        {
            html.Append(string.Join(" • ", metaParts));
            html.Append("<br>");
        }

        plain.AppendLine(titleWithYear);

        // Event type line
        html.Append(CultureInfo.InvariantCulture, $"{eventPrefix}<br>");
        plain.AppendLine(string.Join(" • ", plainMetaParts.Prepend(eventPrefix)));

        // Episodes (for series)
        if (config.EpisodesEnabled && item.Type == "Series")
        {
            ReadOnlyCollection<NlDetailsJson> parsedInfoList = ParseSeriesInfo(item, upcomingItems);
            string seaEps = GetSeasonEpisodeBase(parsedInfoList);
            if (!string.IsNullOrWhiteSpace(seaEps))
            {
                html.Append("📺 ");
                html.Append(seaEps.TrimEnd('\n').Replace("\n", " | ", StringComparison.Ordinal));
                html.Append("<br>");
                plain.AppendLine(seaEps.TrimEnd('\n').Replace("\n", " | ", StringComparison.Ordinal));
            }
        }

        // Upcoming release date
        if (eventType == "upcoming")
        {
            html.Append(CultureInfo.InvariantCulture, $"📅 Release: {item.PremiereYear ?? "N/A"}<br>");
            plain.AppendLine(CultureInfo.InvariantCulture, $"Release: {item.PremiereYear ?? "N/A"}");
        }

        // Description (with a blank line separator)
        if (config.DescriptionEnabled && !string.IsNullOrEmpty(item.SeriesOverview))
        {
            html.Append(CultureInfo.InvariantCulture, $"<br><i>{item.SeriesOverview}</i>");
            plain.AppendLine(item.SeriesOverview);
        }

        return (html.ToString(), plain.ToString());
    }
}
