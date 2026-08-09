using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.Newsletters.Configuration;

/// <summary>
/// Configuration for Matrix client.
/// </summary>
public class MatrixConfiguration : ITemplatedConfiguration
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
    /// Gets or sets a value indicating whether this Matrix configuration is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

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
    /// Gets or sets the template category (e.g., "Matrix").
    /// </summary>
    public string TemplateCategory { get; set; } = "Matrix";

    /// <summary>
    /// Gets or sets a custom body HTML string. If empty, uses the template.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a custom entry HTML string for items. If empty, uses the template.
    /// </summary>
    public string Entry { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the header HTML template for section headers.
    /// Uses template tags with IDs (header-add, header-update, header-delete, header-upcoming).
    /// If empty, the default template file is used.
    /// </summary>
    public string Header { get; set; } = string.Empty;

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

    /// <summary>
    /// Gets or sets a value indicating whether to include upcoming items in the newsletter.
    /// </summary>
    public bool NewsletterOnUpcomingItemEnabled { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to include the featured section in the newsletter.
    /// </summary>
    public bool NewsletterOnFeaturedEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of items shown per newsletter section.
    /// A value of 0 means unlimited (default).
    /// </summary>
    public int MaxItemsPerSection { get; set; } = 0;
}
