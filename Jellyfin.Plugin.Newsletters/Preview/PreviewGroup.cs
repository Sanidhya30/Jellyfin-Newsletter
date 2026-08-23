using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Newsletters.Preview;

/// <summary>
/// Queued files grouped the way the newsletter renders them: one group per title and event type.
/// </summary>
public class PreviewGroup
{
    /// <summary>
    /// Gets or sets the title of the series or movie.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event type (Add, Update or Delete).
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media type (Series or Movie).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the library the item belongs to.
    /// </summary>
    public string LibraryName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the premiere year.
    /// </summary>
    public string PremiereYear { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the poster URL.
    /// </summary>
    public string ImageURL { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the short season and episode summary, for example "S1 E1-3".
    /// </summary>
    public string SeasonSummary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of queued files in this group.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets how many of those files are excluded.
    /// </summary>
    public int ExcludedCount { get; set; }

    /// <summary>
    /// Gets or sets the individual queued files.
    /// </summary>
    public IReadOnlyList<PreviewItem> Items { get; set; } = Array.Empty<PreviewItem>();
}
