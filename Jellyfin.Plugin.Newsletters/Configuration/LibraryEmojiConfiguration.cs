namespace Jellyfin.Plugin.Newsletters.Configuration;

/// <summary>
/// A per-library emoji override for newsletter section headers.
/// </summary>
public class LibraryEmojiConfiguration
{
    /// <summary>
    /// Gets or sets the Jellyfin library (virtual folder) ID this override applies to.
    /// </summary>
    public string LibraryId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the emoji to show in that library's section headers.
    /// An empty value falls back to the default for the library's collection type.
    /// </summary>
    public string Emoji { get; set; } = string.Empty;
}
