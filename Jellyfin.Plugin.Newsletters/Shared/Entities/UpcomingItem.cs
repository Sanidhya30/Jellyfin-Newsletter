using System;

namespace Jellyfin.Plugin.Newsletters.Shared.Entities;

/// <summary>
/// Represents an upcoming media item from Radarr or Sonarr.
/// </summary>
public class UpcomingItem
{
    /// <summary>
    /// Gets or sets the title (movie title or series title).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the overview/description.
    /// </summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the poster image URL.
    /// </summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the air/release date.
    /// </summary>
    public DateTime AirDate { get; set; }

    /// <summary>
    /// Gets or sets the media type ("Movie" or "Episode").
    /// </summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the season number (episodes only).
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode number (episodes only).
    /// </summary>
    public int EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the official/content rating.
    /// </summary>
    public string OfficialRating { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source name (e.g., "My Radarr 4K" or "Sonarr").
    /// </summary>
    public string SourceName { get; set; } = string.Empty;
}
