using System;

namespace Jellyfin.Plugin.Newsletters.Configuration;

/// <summary>
/// Represents a single Radarr instance configuration.
/// </summary>
public class RadarrConfiguration
{
    /// <summary>
    /// Gets or sets the unique identifier for this configuration.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the user-friendly name for this configuration.
    /// </summary>
    public string Name { get; set; } = "Movies";

    /// <summary>
    /// Gets or sets the Radarr base URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Radarr API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
