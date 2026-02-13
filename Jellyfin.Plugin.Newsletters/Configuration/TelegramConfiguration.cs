using System;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.Newsletters.Configuration;

/// <summary>
/// Represents a single Telegram bot/chat configuration.
/// </summary>
public class TelegramConfiguration : INewsletterConfiguration
{
    /// <summary>
    /// Gets or sets the unique identifier for this configuration.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the user-friendly name for this configuration.
    /// </summary>
    public string Name { get; set; } = "Telegram Bot";

    /// <summary>
    /// Gets or sets the Telegram bot token.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Telegram chat ID to send messages to.
    /// </summary>
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether description should be visible in Telegram messages.
    /// </summary>
    public bool DescriptionEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether thumbnail should be visible in Telegram messages.
    /// </summary>
    public bool ThumbnailEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether rating should be visible in Telegram messages.
    /// </summary>
    public bool RatingEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether PG rating should be visible in Telegram messages.
    /// </summary>
    public bool PGRatingEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether duration should be visible in Telegram messages.
    /// </summary>
    public bool DurationEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether episodes list should be visible in Telegram messages.
    /// </summary>
    public bool EpisodesEnabled { get; set; } = true;

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
