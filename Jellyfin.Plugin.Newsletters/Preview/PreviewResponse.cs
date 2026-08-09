using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Newsletters.Preview;

/// <summary>
/// The full preview of the next newsletter.
/// </summary>
public class PreviewResponse
{
    /// <summary>
    /// Gets or sets the queued groups, in the order the newsletter renders them.
    /// </summary>
    public IReadOnlyList<PreviewGroup> Groups { get; set; } = Array.Empty<PreviewGroup>();

    /// <summary>
    /// Gets or sets the total number of queued files.
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Gets or sets how many queued files are excluded.
    /// </summary>
    public int ExcludedItems { get; set; }

    /// <summary>
    /// Gets or sets when the last newsletter was sent, if ever.
    /// </summary>
    public DateTime? LastPublishedDate { get; set; }
}
