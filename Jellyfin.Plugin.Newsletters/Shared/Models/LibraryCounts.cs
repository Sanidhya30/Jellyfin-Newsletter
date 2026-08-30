namespace Jellyfin.Plugin.Newsletters.Shared.Models;

/// <summary>
/// A snapshot of how many media items a set of libraries holds.
/// </summary>
public sealed class LibraryCounts
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryCounts"/> class.
    /// </summary>
    /// <param name="movies">The number of movies.</param>
    /// <param name="series">The number of series.</param>
    /// <param name="episodes">The number of episodes.</param>
    public LibraryCounts(int movies, int series, int episodes)
    {
        Movies = movies;
        Series = series;
        Episodes = episodes;
    }

    /// <summary>
    /// Gets the number of movies.
    /// </summary>
    public int Movies { get; }

    /// <summary>
    /// Gets the number of series.
    /// </summary>
    public int Series { get; }

    /// <summary>
    /// Gets the number of episodes.
    /// </summary>
    public int Episodes { get; }

    /// <summary>
    /// Gets the number of playable items: movies plus episodes.
    /// Series are containers rather than playable media, so they are deliberately excluded.
    /// </summary>
    public int Items => Movies + Episodes;
}
