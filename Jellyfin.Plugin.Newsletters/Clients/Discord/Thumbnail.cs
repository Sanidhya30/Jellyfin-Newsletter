using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Newsletters.Clients.Discord;

/// <summary>
/// Represents a thumbnail image for a Discord embed.
/// </summary>
public class Thumbnail
{
    /// <summary>
    /// Gets or sets the URL of the thumbnail image.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
