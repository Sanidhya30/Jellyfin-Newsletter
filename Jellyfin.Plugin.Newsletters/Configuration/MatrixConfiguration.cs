using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.Newsletters.Configuration;

/// <summary>
/// Configuration for Matrix client.
/// </summary>
public class MatrixConfiguration : INewsletterConfiguration
{
    /// <summary>
    /// Gets or sets the name of the Matrix configuration.
    /// </summary>
    public string Name { get; set; } = "Matrix Bot";

    /// <summary>
    /// Gets or sets the unique identifier for the configuration.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Matrix Homeserver URL.
    /// </summary>
    public string HomeserverUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Access Token for the Matrix user/bot.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Room ID to send messages to.
    /// </summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the template category (e.g., "Modern").
    /// </summary>
    public string TemplateCategory { get; set; } = "Modern";

    /// <summary>
    /// Gets or sets a custom body HTML string. If empty, uses the template.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a custom entry HTML string for items. If empty, uses the template.
    /// </summary>
    public string Entry { get; set; } = string.Empty;

    /// <inheritdoc/>
    public Collection<string> SelectedSeriesLibraries { get; set; } = new();

    /// <inheritdoc/>
    public Collection<string> SelectedMoviesLibraries { get; set; } = new();

    /// <inheritdoc/>
    public bool NewsletterOnItemAddedEnabled { get; set; } = true;

    /// <inheritdoc/>
    public bool NewsletterOnItemUpdatedEnabled { get; set; }

    /// <inheritdoc/>
    public bool NewsletterOnItemDeletedEnabled { get; set; } = true;

    /// <inheritdoc/>
    public bool NewsletterOnUpcomingItemEnabled { get; set; }
}
