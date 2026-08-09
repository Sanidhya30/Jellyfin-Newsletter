using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Newsletters.Featured;

/// <summary>
/// Resolves the admin's pinned library items into newsletter entries.
/// </summary>
/// <remarks>
/// Featured items are read live from the library at send time rather than from the queue table,
/// the same way upcoming items are, so anything in the library can be featured regardless of when
/// it was added and nothing about the queue changes.
/// </remarks>
public static class FeaturedItems
{
    /// <summary>
    /// The event type featured entries carry through the newsletter pipeline.
    /// </summary>
    public const string EventType = "featured";

    /// <summary>
    /// The single section heading featured entries are grouped under, spanning all libraries.
    /// </summary>
    public const string SectionName = "Featured";

    /// <summary>
    /// Builds newsletter entries for the given library item IDs.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="logger">The logger used to report items that cannot be resolved.</param>
    /// <param name="hostname">The configured server URL, used to build poster URLs.</param>
    /// <param name="itemIds">The pinned Jellyfin item IDs, in the order they should appear.</param>
    /// <returns>The resolved entries. IDs that no longer exist are skipped.</returns>
    public static Collection<JsonFileObj> Build(
        ILibraryManager libraryManager,
        Logger logger,
        string? hostname,
        IEnumerable<string>? itemIds)
    {
        var featured = new Collection<JsonFileObj>();

        foreach (var rawId in itemIds ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(rawId) || !Guid.TryParse(rawId, out var guid))
            {
                continue;
            }

            try
            {
                var item = libraryManager.GetItemById(guid);
                if (item is null)
                {
                    logger.Warn($"Featured item {rawId} no longer exists in the library - skipping");
                    continue;
                }

                featured.Add(ToEntry(item, libraryManager, hostname));
            }
            catch (Exception ex)
            {
                logger.Error($"Could not resolve featured item {rawId}: {ex.Message}");
            }
        }

        return featured;
    }

    private static JsonFileObj ToEntry(BaseItem item, ILibraryManager libraryManager, string? hostname)
    {
        var entry = new JsonFileObj
        {
            Title = item.Name ?? string.Empty,
            SeriesOverview = item.Overview ?? string.Empty,
            ItemID = item.Id.ToString("N", CultureInfo.InvariantCulture),
            Type = item is Movie ? "Movie" : "Series",
            OfficialRating = item.OfficialRating ?? string.Empty,
            CommunityRating = item.CommunityRating ?? 0.0f,
            PosterPath = item.PrimaryImagePath ?? string.Empty,
            EventType = EventType,
            Genres = item.Genres is null ? string.Empty : string.Join(", ", item.Genres),
            LibraryId = ResolveLibraryId(item, libraryManager),
            // A featured entry is a whole title, not an episode. -1 keeps it out of the
            // season/episode line the way the scanner marks items with no episode number.
            Season = -1,
            Episode = -1
        };

        if (item.ProductionYear.HasValue)
        {
            entry.PremiereYear = item.ProductionYear.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (item.RunTimeTicks.HasValue)
        {
            entry.RunTime = (int)TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalMinutes;
        }

        // Unlike scanned items there is no cached TMDB URL to reuse, so point at the server's
        // own image endpoint. Attachment mode uses PosterPath above and ignores this.
        if (!string.IsNullOrWhiteSpace(hostname))
        {
            entry.ImageURL = $"{hostname.TrimEnd('/')}/Items/{entry.ItemID}/Images/Primary";
        }

        return entry;
    }

    private static string ResolveLibraryId(BaseItem item, ILibraryManager libraryManager)
    {
        // The real library ID is kept so per-client library filtering still applies to featured
        // items; the single-section grouping is done by event type instead.
        try
        {
            var folder = libraryManager.GetCollectionFolders(item).FirstOrDefault();
            if (folder is not null)
            {
                return folder.Id.ToString("N", CultureInfo.InvariantCulture);
            }
        }
        catch (Exception)
        {
            // fall through - an unresolved library just means no library filtering
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets a value indicating whether the item is a type that can be featured.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>True for movies and series.</returns>
    public static bool IsFeaturable(BaseItem item)
    {
        return item is Movie || item is Series;
    }
}
