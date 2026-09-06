namespace Jellyfin.Plugin.Newsletters.Shared.Models;

/// <summary>
/// How much media a single library held when the last newsletter was sent.
/// </summary>
/// <remarks>
/// Counts are stored per library rather than as one total so that changing a configuration's
/// library selection cannot fake a delta. A library that was added or removed since the last
/// newsletter simply has no counterpart to compare against, and is left out of the change.
/// A library is either a movie library or a series library - never both - so only the counts
/// that library can contribute are meaningful.
/// </remarks>
public sealed class StoredLibraryCount
{
    /// <summary>
    /// Gets or sets the Jellyfin library (virtual folder) ID this snapshot describes.
    /// </summary>
    public string LibraryId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of titles the library held: movies for a movie library,
    /// series for a series library.
    /// </summary>
    public int Titles { get; set; }

    /// <summary>
    /// Gets or sets the number of episodes the library held. Only meaningful for a series
    /// library; a movie library holds no episodes.
    /// </summary>
    public int Episodes { get; set; }
}
