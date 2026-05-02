using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Newsletters.Clients.Matrix;

/// <summary>
/// JSON payload representation for a Matrix message.
/// </summary>
public class MatrixPayload
{
    /// <summary>
    /// Gets or sets the message type.
    /// </summary>
    [JsonPropertyName("msgtype")]
    public string MsgType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the plaintext fallback body.
    /// </summary>
    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the custom format (e.g. org.matrix.custom.html).
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the rich formatted HTML body.
    /// </summary>
    [JsonPropertyName("formatted_body")]
    public string FormattedBody { get; set; } = string.Empty;
}
