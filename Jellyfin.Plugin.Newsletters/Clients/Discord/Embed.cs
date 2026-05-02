using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Newsletters.Clients.Discord;

/// <summary>
/// Represents a Discord embed message with title, description, fields, and other formatting options.
/// </summary>
public class Embed
{
    /// <summary>
    /// Gets or sets the title of the embed.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the URL that the embed title links to.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the color of the embed as an integer value.
    /// </summary>
    [JsonPropertyName("color")]
    public int Color { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the embed in ISO 8601 format.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the description text of the embed.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the collection of fields to display in the embed.
    /// </summary>
    [JsonPropertyName("fields")]
    public ReadOnlyCollection<EmbedField>? Fields { get; set; }

    /// <summary>
    /// Gets or sets the thumbnail image for the embed.
    /// </summary>
    [JsonPropertyName("thumbnail")]
    public Thumbnail? Thumbnail { get; set; }
}
