namespace Jellyfin.Plugin.Newsletters.Preview;

/// <summary>
/// A single queued file (one episode, or one movie) awaiting the next newsletter.
/// </summary>
public class PreviewItem
{
    /// <summary>
    /// Gets or sets the filename, which is the primary key of the queue table.
    /// </summary>
    public string Filename { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the season number, or -1 for movies.
    /// </summary>
    public int Season { get; set; }

    /// <summary>
    /// Gets or sets the episode number, or -1 for movies.
    /// </summary>
    public int Episode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this item is excluded from the next newsletter.
    /// </summary>
    public bool Excluded { get; set; }
}
