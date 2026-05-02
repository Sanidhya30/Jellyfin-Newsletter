using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Newsletters.Clients.Discord;

/// <summary>
/// Represents the payload structure for sending messages to Discord webhooks.
/// </summary>
public class DiscordPayload
{
    /// <summary>
    /// Gets or sets the username to display for the webhook message.
    /// </summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the collection of embeds to include in the webhook message.
    /// </summary>
    [JsonPropertyName("embeds")]
    public ReadOnlyCollection<Embed>? Embeds { get; set; }
}
