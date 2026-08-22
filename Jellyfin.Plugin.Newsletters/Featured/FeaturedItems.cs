using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Newsletters.Scanner;
using Jellyfin.Plugin.Newsletters.Shared.Database;
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

    // Same provider-to-TMDB key mapping the scanner uses when it builds an entry.
    private static readonly Dictionary<string, string> AllowedExternalIds = new()
    {
        { "Imdb", "imdb_id" },
        { "Tmdb", "tmdb" },
        { "Tvdb", "tvdb_id" },
    };

    /// <summary>
    /// The single section heading featured entries are grouped under, spanning all libraries.
    /// </summary>
    public const string SectionName = "Featured";

    /// <summary>
    /// Builds newsletter entries for the given library item IDs.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="logger">The logger used to report items that cannot be resolved.</param>
    /// <param name="db">The database searched for an already known poster URL.</param>
    /// <param name="imageHandler">The handler used to look up and fetch poster URLs.</param>
    /// <param name="hostname">The configured server URL, used as the poster URL of last resort.</param>
    /// <param name="itemIds">The pinned Jellyfin item IDs, in the order they should appear.</param>
    /// <returns>The resolved entries. IDs that no longer exist are skipped.</returns>
    public static Collection<JsonFileObj> Build(
        ILibraryManager libraryManager,
        Logger logger,
        SQLiteDatabase db,
        PosterImageHandler imageHandler,
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

                featured.Add(ToEntry(item, libraryManager, logger, db, imageHandler, hostname));
            }
            catch (Exception ex)
            {
                logger.Error($"Could not resolve featured item {rawId}: {ex.Message}");
            }
        }

        return featured;
    }

    private static JsonFileObj ToEntry(BaseItem item, ILibraryManager libraryManager, Logger logger, SQLiteDatabase db, PosterImageHandler imageHandler, string? hostname)
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

        // The scanner keys its TMDB lookups off these, so carry them over and the same
        // resolution works for a featured pick. Attachment mode uses PosterPath and ignores it.
        foreach (var kvp in item.ProviderIds)
        {
            if (AllowedExternalIds.TryGetValue(kvp.Key, out var mappedKey))
            {
                entry.ExternalIds[mappedKey] = kvp.Value;
            }
        }

        entry.ImageURL = ResolveImageUrl(entry, logger, db, imageHandler, hostname);

        return entry;
    }

    private static string ResolveImageUrl(JsonFileObj entry, Logger logger, SQLiteDatabase db, PosterImageHandler imageHandler, string? hostname)
    {
        // Prefer a URL an earlier scan already resolved for this title, exactly as the scanner does.
        try
        {
            db.CreateConnection();
            string cached = imageHandler.FindCachedImageUrl(db, entry.Title);
            if (!string.IsNullOrEmpty(cached))
            {
                return cached;
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"Could not check for a cached image URL for '{entry.Title}': {ex.Message}");
        }
        finally
        {
            db.CloseConnection();
        }

        // Nothing cached - ask TMDB the same way a scan would.
        try
        {
            string fetched = imageHandler.FetchImagePoster(entry);
            if (!string.IsNullOrEmpty(fetched) && fetched != "429" && fetched != "ERR")
            {
                return fetched;
            }

            logger.Warn($"Could not obtain a poster URL for featured item '{entry.Title}'");
        }
        catch (Exception ex)
        {
            logger.Error($"Poster lookup failed for featured item '{entry.Title}': {ex.Message}");
        }

        // Last resort: the server's own image endpoint, which needs a reachable hostname.
        return string.IsNullOrWhiteSpace(hostname)
            ? string.Empty
            : $"{hostname.TrimEnd('/')}/Items/{entry.ItemID}/Images/Primary";
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
}
