namespace Jellyfin.Plugin.Newsletters.Shared.Entities;

/// <summary>
/// Represents the details of a newsletter item.
/// </summary>
public class NlDetailsJson
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NlDetailsJson"/> class.
    /// </summary>
    public NlDetailsJson()
    {
        Title = string.Empty;
        Season = 0;
        Episode = 0;
        EpisodeRange = string.Empty;
    }

    /// <summary>
    /// Gets or sets the title of the item.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the season of the item.
    /// </summary>
    public int Season { get; set; }

    /// <summary>
    /// Gets or sets the episode of the item.
    /// </summary>
    public int Episode { get; set; }

    /// <summary>
    /// Gets or sets the episodes range of the item.
    /// </summary>
    public string EpisodeRange { get; set; }
}