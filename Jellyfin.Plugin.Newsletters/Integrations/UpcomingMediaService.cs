using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Entities;

namespace Jellyfin.Plugin.Newsletters.Integrations;

/// <summary>
/// Service that fetches upcoming media from Radarr and Sonarr APIs.
/// </summary>
public class UpcomingMediaService
{
    private static readonly HttpClient HttpClient = new();
    private readonly Logger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpcomingMediaService"/> class.
    /// </summary>
    /// <param name="loggerInstance">The logger instance.</param>
    public UpcomingMediaService(Logger loggerInstance)
    {
        logger = loggerInstance;
    }

    private static PluginConfiguration Config => Plugin.Instance!.Configuration;

    /// <summary>
    /// Gets all upcoming items from all configured Radarr and Sonarr instances.
    /// </summary>
    /// <returns>A list of upcoming items sorted by air date.</returns>
    public async Task<List<UpcomingItem>> GetAllUpcomingAsync()
    {
        var items = new List<UpcomingItem>();

        foreach (var radarrConfig in Config.RadarrConfigurations)
        {
            var movies = await GetUpcomingMoviesAsync(radarrConfig.Url, radarrConfig.ApiKey, radarrConfig.Name).ConfigureAwait(false);
            items.AddRange(movies);
        }

        foreach (var sonarrConfig in Config.SonarrConfigurations)
        {
            var episodes = await GetUpcomingEpisodesAsync(sonarrConfig.Url, sonarrConfig.ApiKey, sonarrConfig.Name).ConfigureAwait(false);
            items.AddRange(episodes);
        }

        // Sort all upcoming items by air date
        items.Sort((a, b) => a.AirDate.CompareTo(b.AirDate));
        return items;
    }

    /// <summary>
    /// Fetches upcoming movies from a Radarr instance.
    /// </summary>
    /// <param name="baseUrl">The Radarr base URL.</param>
    /// <param name="apiKey">The Radarr API key.</param>
    /// <param name="sourceName">The name of this Radarr instance.</param>
    /// <returns>A list of upcoming movie items.</returns>
    public async Task<List<UpcomingItem>> GetUpcomingMoviesAsync(string baseUrl, string apiKey, string sourceName)
    {
        var items = new List<UpcomingItem>();

        try
        {
            var trimmedUrl = baseUrl.TrimEnd('/');
            var start = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = DateTime.UtcNow.AddDays(Config.UpcomingDaysAhead).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var url = $"{trimmedUrl}/api/v3/calendar?start={start}&end={end}&unmonitored=false&apikey={apiKey}";

            logger.Debug($"Fetching upcoming movies from '{sourceName}': {trimmedUrl}/api/v3/calendar?start={start}&end={end}");

            var response = await HttpClient.GetStringAsync(new Uri(url)).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(response);

            foreach (var movie in doc.RootElement.EnumerateArray())
            {
                var item = new UpcomingItem
                {
                    Title = movie.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty,
                    Overview = movie.TryGetProperty("overview", out var overviewProp) ? overviewProp.GetString() ?? string.Empty : string.Empty,
                    MediaType = "Movie",
                    OfficialRating = movie.TryGetProperty("certification", out var certProp) ? certProp.GetString() ?? string.Empty : string.Empty,
                    SourceName = sourceName
                };

                // Parse release date — try digitalRelease first, then physicalRelease, then inCinemas
                if (movie.TryGetProperty("digitalRelease", out var digitalRelease) && digitalRelease.ValueKind != JsonValueKind.Null)
                {
                    item.AirDate = digitalRelease.GetDateTime();
                }
                else if (movie.TryGetProperty("physicalRelease", out var physicalRelease) && physicalRelease.ValueKind != JsonValueKind.Null)
                {
                    item.AirDate = physicalRelease.GetDateTime();
                }
                else if (movie.TryGetProperty("inCinemas", out var inCinemas) && inCinemas.ValueKind != JsonValueKind.Null)
                {
                    item.AirDate = inCinemas.GetDateTime();
                }

                // Try to get poster image
                if (movie.TryGetProperty("images", out var images))
                {
                    foreach (var image in images.EnumerateArray())
                    {
                        if (image.TryGetProperty("coverType", out var coverType) &&
                            string.Equals(coverType.GetString(), "poster", StringComparison.OrdinalIgnoreCase))
                        {
                            var remoteUrl = image.TryGetProperty("remoteUrl", out var remoteProp) ? remoteProp.GetString() ?? string.Empty : string.Empty;
                            if (!string.IsNullOrEmpty(remoteUrl))
                            {
                                item.ImageUrl = remoteUrl;
                            }

                            break;
                        }
                    }
                }

                items.Add(item);
            }

            logger.Debug($"Found {items.Count} upcoming movies from '{sourceName}'");
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to fetch upcoming movies from '{sourceName}': {ex.Message}");
        }

        return items;
    }

    /// <summary>
    /// Fetches upcoming episodes from a Sonarr instance.
    /// </summary>
    /// <param name="baseUrl">The Sonarr base URL.</param>
    /// <param name="apiKey">The Sonarr API key.</param>
    /// <param name="sourceName">The name of this Sonarr instance.</param>
    /// <returns>A list of upcoming episode items.</returns>
    public async Task<List<UpcomingItem>> GetUpcomingEpisodesAsync(string baseUrl, string apiKey, string sourceName)
    {
        var items = new List<UpcomingItem>();

        try
        {
            var trimmedUrl = baseUrl.TrimEnd('/');
            var start = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = DateTime.UtcNow.AddDays(Config.UpcomingDaysAhead).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var url = $"{trimmedUrl}/api/v3/calendar?start={start}&end={end}&includeSeries=true&unmonitored=false&apikey={apiKey}";

            logger.Debug($"Fetching upcoming episodes from '{sourceName}': {trimmedUrl}/api/v3/calendar?start={start}&end={end}");

            var response = await HttpClient.GetStringAsync(new Uri(url)).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(response);

            foreach (var episode in doc.RootElement.EnumerateArray())
            {
                var item = new UpcomingItem
                {
                    Overview = episode.TryGetProperty("overview", out var overviewProp) ? overviewProp.GetString() ?? string.Empty : string.Empty,
                    SeasonNumber = episode.TryGetProperty("seasonNumber", out var seasonProp) ? seasonProp.GetInt32() : 0,
                    EpisodeNumber = episode.TryGetProperty("episodeNumber", out var episodeProp) ? episodeProp.GetInt32() : 0,
                    MediaType = "Episode",
                    SourceName = sourceName
                };

                // Parse air date
                if (episode.TryGetProperty("airDateUtc", out var airDate) && airDate.ValueKind != JsonValueKind.Null)
                {
                    item.AirDate = airDate.GetDateTime();
                }
                else if (episode.TryGetProperty("airDate", out var airDateStr) && airDateStr.ValueKind != JsonValueKind.Null)
                {
                    if (DateTime.TryParse(airDateStr.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    {
                        item.AirDate = parsed;
                    }
                }

                // Get series info
                if (episode.TryGetProperty("series", out var series))
                {
                    item.Title = series.TryGetProperty("title", out var seriesTitleProp) ? seriesTitleProp.GetString() ?? string.Empty : string.Empty;
                    item.OfficialRating = series.TryGetProperty("certification", out var certProp) ? certProp.GetString() ?? string.Empty : string.Empty;

                    // Try to get poster image from series
                    if (series.TryGetProperty("images", out var images))
                    {
                        foreach (var image in images.EnumerateArray())
                        {
                            if (image.TryGetProperty("coverType", out var coverType) &&
                                string.Equals(coverType.GetString(), "poster", StringComparison.OrdinalIgnoreCase))
                            {
                                var remoteUrl = image.TryGetProperty("remoteUrl", out var remoteProp) ? remoteProp.GetString() ?? string.Empty : string.Empty;
                                if (!string.IsNullOrEmpty(remoteUrl))
                                {
                                    item.ImageUrl = remoteUrl;
                                }

                                break;
                            }
                        }
                    }
                }

                items.Add(item);
            }

            logger.Debug($"Found {items.Count} upcoming episodes from '{sourceName}'");
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to fetch upcoming episodes from '{sourceName}': {ex.Message}");
        }

        return items;
    }

    /// <summary>
    /// Tests the connection to a Radarr or Sonarr instance.
    /// </summary>
    /// <param name="url">The base URL of the instance.</param>
    /// <param name="apiKey">The API key for the instance.</param>
    /// <returns>True if the connection is successful; otherwise, false.</returns>
    public async Task<bool> TestConnectionAsync(string url, string apiKey)
    {
        try
        {
            var baseUrl = url.TrimEnd('/');
            var requestUrl = $"{baseUrl}/api/v3/system/status?apikey={apiKey}";
            logger.Debug($"Testing connection: {baseUrl}/api/v3/system/status");
            var response = await HttpClient.GetAsync(new Uri(requestUrl)).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.Error($"Connection test failed for {url}: {ex.Message}");
            return false;
        }
    }
}
