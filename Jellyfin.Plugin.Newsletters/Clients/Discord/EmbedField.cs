using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Newsletters.Clients.Discord;

/// <summary>
/// Represents a field within a Discord embed with name, value, and inline properties.
/// </summary>
public class EmbedField
{
    /// <summary>
    /// Gets or sets the name of the embed field.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the value of the embed field.
    /// </summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field should be displayed inline.
    /// </summary>
    [JsonPropertyName("inline")]
    public bool Inline { get; set; }
}
