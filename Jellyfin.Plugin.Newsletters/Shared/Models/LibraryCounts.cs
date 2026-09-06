namespace Jellyfin.Plugin.Newsletters.Shared.Models;

/// <summary>
/// The combined media totals for a set of libraries, as rendered into a newsletter.
/// </summary>
/// <remarks>
/// A computed value, never persisted. The counts remembered between newsletters are stored per
/// library as <see cref="StoredLibraryCount"/>.
/// </remarks>
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

    /// <summary>
    /// Gets a sample set of counts for test newsletters, where real library totals are not
    /// meaningful. Paired with a zero baseline so every delta renders as "+1".
    /// </summary>
    /// <returns>A sample <see cref="LibraryCounts"/> holding one of each item type.</returns>
    public static LibraryCounts GetTestObj()
    {
        return new LibraryCounts(1, 1, 1);
    }
}
