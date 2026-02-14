using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.Newsletters.Configuration;

/// <summary>
/// Interface for newsletter configurations that support item filtering.
/// </summary>
public interface INewsletterConfiguration
{
    /// <summary>
    /// Gets the collection of selected series libraries.
    /// </summary>
    Collection<string> SelectedSeriesLibraries { get; }

    /// <summary>
    /// Gets the collection of selected movies libraries.
    /// </summary>
    Collection<string> SelectedMoviesLibraries { get; }

    /// <summary>
    /// Gets a value indicating whether to send newsletter on item added.
    /// </summary>
    bool NewsletterOnItemAddedEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether to send newsletter on item updated.
    /// </summary>
    bool NewsletterOnItemUpdatedEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether to send newsletter on item deleted.
    /// </summary>
    bool NewsletterOnItemDeletedEnabled { get; }
}
