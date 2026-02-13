using System;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.Newsletters.Configuration;

/// <summary>
/// Represents a single Discord webhook configuration.
/// </summary>
public class DiscordConfiguration : INewsletterConfiguration
{
    /// <summary>
    /// Gets or sets the unique identifier for this configuration.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the user-friendly name for this configuration.
    /// </summary>
    public string Name { get; set; } = "Discord Webhook";

    /// <summary>
    /// Gets or sets the Discord webhook URL.
    /// </summary>
    public string WebhookURL { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Discord webhook display name.
    /// </summary>
    public string WebhookName { get; set; } = "Jellyfin Newsletter";

    /// <summary>
    /// Gets or sets a value indicating whether description should be visible in Discord embed.
    /// </summary>
    public bool DescriptionEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether thumbnail should be visible in Discord embed.
    /// </summary>
    public bool ThumbnailEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether rating should be visible in Discord embed.
    /// </summary>
    public bool RatingEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether PG rating should be visible in Discord embed.
    /// </summary>
    public bool PGRatingEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether duration should be visible in Discord embed.
    /// </summary>
    public bool DurationEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether episodes list should be visible in Discord embed.
    /// </summary>
    public bool EpisodesEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the embed color for series add events.
    /// </summary>
    public string SeriesAddEmbedColor { get; set; } = "#00ff00";

    /// <summary>
    /// Gets or sets the embed color for series delete events.
    /// </summary>
    public string SeriesDeleteEmbedColor { get; set; } = "#ff0000";

    /// <summary>
    /// Gets or sets the embed color for series update events.
    /// </summary>
    public string SeriesUpdateEmbedColor { get; set; } = "#0000ff";

    /// <summary>
    /// Gets or sets the embed color for movies add events.
    /// </summary>
    public string MoviesAddEmbedColor { get; set; } = "#00ff00";

    /// <summary>
    /// Gets or sets the embed color for movies delete events.
    /// </summary>
    public string MoviesDeleteEmbedColor { get; set; } = "#ff0000";

    /// <summary>
    /// Gets or sets the embed color for movies update events.
    /// </summary>
    public string MoviesUpdateEmbedColor { get; set; } = "#0000ff";

    /// <summary>
    /// Gets or sets the collection of selected series libraries.
    /// </summary>
    public Collection<string> SelectedSeriesLibraries { get; set; } = new Collection<string>();

    /// <summary>
    /// Gets or sets the collection of selected movies libraries.
    /// </summary>
    public Collection<string> SelectedMoviesLibraries { get; set; } = new Collection<string>();

    /// <summary>
    /// Gets or sets a value indicating whether to send newsletter on item added.
    /// </summary>
    public bool NewsletterOnItemAddedEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to send newsletter on item updated.
    /// </summary>
    public bool NewsletterOnItemUpdatedEnabled { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to send newsletter on item deleted.
    /// </summary>
    public bool NewsletterOnItemDeletedEnabled { get; set; } = true;
}
