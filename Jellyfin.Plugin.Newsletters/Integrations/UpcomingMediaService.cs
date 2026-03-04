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
    public async Task<List<JsonFileObj>> GetAllUpcomingAsync()
    {
        var items = new List<JsonFileObj>();

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

        // Sort all upcoming items by air date (parsing dd-MM-yyyy back to DateTime)
        items.Sort((a, b) => 
        {
            var dateA = DateTime.TryParseExact(a.PremiereYear, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtA) ? dtA : DateTime.MaxValue;
            var dateB = DateTime.TryParseExact(b.PremiereYear, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtB) ? dtB : DateTime.MaxValue;
            return dateA.CompareTo(dateB);
        });
        return items;
    }

    /// <summary>
    /// Fetches upcoming movies from a Radarr instance.
    /// </summary>
    /// <param name="baseUrl">The Radarr base URL.</param>
    /// <param name="apiKey">The Radarr API key.</param>
    /// <param name="sourceName">The name of this Radarr instance.</param>
    /// <returns>A list of upcoming movie items.</returns>
    public async Task<List<JsonFileObj>> GetUpcomingMoviesAsync(string baseUrl, string apiKey, string sourceName)
    {
        var items = new List<JsonFileObj>();

        try
        {
            var trimmedUrl = baseUrl.TrimEnd('/');
            var startDateTime = DateTime.UtcNow.Date;
            var endDateTime = startDateTime.AddDays(Config.UpcomingDaysAhead);
            var start = startDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = endDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var url = $"{trimmedUrl}/api/v3/calendar?start={start}&end={end}&unmonitored=false&apikey={apiKey}";

            logger.Debug($"Fetching upcoming movies from '{sourceName}': {trimmedUrl}/api/v3/calendar?start={start}&end={end}");

            var response = await HttpClient.GetStringAsync(new Uri(url)).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(response);

            foreach (var movie in doc.RootElement.EnumerateArray())
            {
                var item = new JsonFileObj
                {
                    Title = movie.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty,
                    SeriesOverview = movie.TryGetProperty("overview", out var overviewProp) ? overviewProp.GetString() ?? string.Empty : string.Empty,
                    Type = "Movie",
                    OfficialRating = movie.TryGetProperty("certification", out var certProp) ? certProp.GetString() ?? string.Empty : string.Empty,
                    RunTime = movie.TryGetProperty("runtime", out var runtimeProp) ? runtimeProp.GetInt32() : 0,
                    EventType = "upcoming",
                    LibraryId = sourceName
                };

                DateTime? validReleaseDate = null;

                // Parse release date - try digitalRelease first, then physicalRelease
                // Only consider dates that fall strictly within our requested start/end window
                if (movie.TryGetProperty("digitalRelease", out var digitalRelease) && digitalRelease.ValueKind != JsonValueKind.Null)
                {
                    var date = digitalRelease.GetDateTime().ToUniversalTime().Date;
                    if (date >= startDateTime && date <= endDateTime)
                    {
                        validReleaseDate = date;
                    }
                }

                if (!validReleaseDate.HasValue && movie.TryGetProperty("physicalRelease", out var physicalRelease) && physicalRelease.ValueKind != JsonValueKind.Null)
                {
                    var date = physicalRelease.GetDateTime().ToUniversalTime().Date;
                    if (date >= startDateTime && date <= endDateTime)
                    {
                        validReleaseDate = date;
                    }
                }

                // If neither digital nor physical release is within the window, skip this movie
                if (!validReleaseDate.HasValue)
                {
                    continue;
                }

                item.PremiereYear = validReleaseDate.Value.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

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
                                item.ImageURL = remoteUrl;
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
    public async Task<List<JsonFileObj>> GetUpcomingEpisodesAsync(string baseUrl, string apiKey, string sourceName)
    {
        var items = new List<JsonFileObj>();

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
                var item = new JsonFileObj
                {
                    SeriesOverview = episode.TryGetProperty("overview", out var overviewProp) ? overviewProp.GetString() ?? string.Empty : string.Empty,
                    Season = episode.TryGetProperty("seasonNumber", out var seasonProp) ? seasonProp.GetInt32() : 0,
                    Episode = episode.TryGetProperty("episodeNumber", out var episodeProp) ? episodeProp.GetInt32() : 0,
                    Type = "Series",
                    EventType = "upcoming",
                    LibraryId = sourceName
                };

                // Parse air date
                if (episode.TryGetProperty("airDateUtc", out var airDate) && airDate.ValueKind != JsonValueKind.Null)
                {
                    item.PremiereYear = airDate.GetDateTime().ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
                }
                else if (episode.TryGetProperty("airDate", out var airDateStr) && airDateStr.ValueKind != JsonValueKind.Null)
                {
                    if (DateTime.TryParse(airDateStr.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    {
                        item.PremiereYear = parsed.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
                    }
                }

                // Get series info
                if (episode.TryGetProperty("series", out var series))
                {
                    item.Title = series.TryGetProperty("title", out var seriesTitleProp) ? seriesTitleProp.GetString() ?? string.Empty : string.Empty;
                    item.OfficialRating = series.TryGetProperty("certification", out var certProp) ? certProp.GetString() ?? string.Empty : string.Empty;
                    item.RunTime = series.TryGetProperty("runtime", out var runtimeProp) ? runtimeProp.GetInt32() : 0;

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
                                    item.ImageURL = remoteUrl;
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
