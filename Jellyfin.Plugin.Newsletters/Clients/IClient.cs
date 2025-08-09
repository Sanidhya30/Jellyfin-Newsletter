namespace Jellyfin.Plugin.Newsletters.Clients;

/// <summary>
/// Represents a client that can send newsletters and archive newsletter data.
/// </summary>
public interface IClient
{
    /// <summary>
    /// Sends the newsletter.
    /// </summary>
    /// <returns>True if the newsletter was sent successfully; otherwise, false.</returns>
    bool Send();

    /// <summary>
    /// Copies the newsletter data to the archive.
    /// </summary>
    void CopyNewsletterDataToArchive();
}