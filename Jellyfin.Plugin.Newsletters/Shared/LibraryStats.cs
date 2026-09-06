using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Newsletters.Shared;

/// <summary>
/// Counts the media a newsletter configuration's selected libraries hold.
/// </summary>
/// <remarks>
/// Every client configuration carries its own library selection, so counts are resolved per
/// configuration rather than server-wide. Results are cached by the selected library IDs, which
/// means two configurations covering the same libraries share one query while a configuration
/// covering different libraries gets its own numbers.
/// </remarks>
public static class LibraryStats
{
    // One send builds a message per client configuration, and each build asks for counts.
    // A short TTL keeps those consistent with each other without pinning a stale value across
    // a week of test sends.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private static readonly ConcurrentDictionary<string, CacheEntry> CountCache = new();

    /// <summary>
    /// Gets the per-library media counts for the libraries selected on a newsletter configuration.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="logger">The logger used to report lookup failures.</param>
    /// <param name="config">The configuration whose library selection should be counted.</param>
    /// <returns>
    /// The snapshot, or null when the library could not be walked. A configuration with nothing
    /// selected returns an empty snapshot rather than null, so callers can tell "empty" from
    /// "failed" - which matters when the result is about to be stored as the baseline for the
    /// next newsletter.
    /// </returns>
    internal static Snapshot? GetSnapshot(ILibraryManager libraryManager, Logger logger, INewsletterConfiguration config)
    {
        if (config is null)
        {
            return new Snapshot();
        }

        var movieLibraries = ParseIds(config.SelectedMoviesLibraries);
        var seriesLibraries = ParseIds(config.SelectedSeriesLibraries);
        string cacheKey = BuildCacheKey(movieLibraries, seriesLibraries);

        if (CountCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CapturedAt < CacheLifetime)
        {
            logger.Debug($"Using cached library counts for key '{cacheKey}'");
            return cached.Snapshot;
        }

        logger.Debug($"Library selection for counts - Movies: [{string.Join(", ", movieLibraries)}], Series: [{string.Join(", ", seriesLibraries)}]");

        var snapshot = Compute(libraryManager, logger, movieLibraries, seriesLibraries);
        if (snapshot is null)
        {
            // Not cached - a transient failure should not stick around for the TTL.
            return null;
        }

        CountCache[cacheKey] = new CacheEntry(DateTime.UtcNow, snapshot);

        var totals = snapshot.Totals;
        logger.Debug($"Library counts resolved - Movies: {totals.Movies}, Series: {totals.Series}, Episodes: {totals.Episodes}");
        return snapshot;
    }

    /// <summary>
    /// Drops every cached count so the next request re-queries the library.
    /// </summary>
    public static void ClearCache()
    {
        CountCache.Clear();
    }

    private static Snapshot? Compute(ILibraryManager libraryManager, Logger logger, Guid[] movieLibraries, Guid[] seriesLibraries)
    {
        // An empty selection counts as a legitimate zero, so say so plainly - otherwise a
        // configuration with no libraries picked looks identical to a broken lookup.
        if (movieLibraries.Length == 0)
        {
            logger.Warn("No movie libraries are selected on this configuration - the movie count will be 0.");
        }

        if (seriesLibraries.Length == 0)
        {
            logger.Warn("No series libraries are selected on this configuration - the series and episode counts will be 0.");
        }

        try
        {
            var snapshot = new Snapshot();

            foreach (var libraryId in movieLibraries)
            {
                var counted = CountInLibrary(libraryManager, logger, libraryId);
                snapshot.MovieLibraries.Add(new StoredLibraryCount
                {
                    LibraryId = libraryId.ToString("N", CultureInfo.InvariantCulture),
                    Titles = counted.Movies
                });
            }

            foreach (var libraryId in seriesLibraries)
            {
                var counted = CountInLibrary(libraryManager, logger, libraryId);
                snapshot.SeriesLibraries.Add(new StoredLibraryCount
                {
                    LibraryId = libraryId.ToString("N", CultureInfo.InvariantCulture),
                    Titles = counted.Series,
                    Episodes = counted.Episodes
                });
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            logger.Error($"Error counting library items: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Counts the media in one library by walking its children.
    /// </summary>
    /// <remarks>
    /// The hierarchy is walked rather than queried. An <see cref="InternalItemsQuery"/> scoped by
    /// ParentId or TopParentIds returns nothing for a library's collection-folder ID, so the
    /// filter column does not hold what the configuration stores. Walking the folder counts what
    /// is actually there, and one pass yields all three totals.
    /// </remarks>
    private static (int Movies, int Series, int Episodes) CountInLibrary(ILibraryManager libraryManager, Logger logger, Guid libraryId)
    {
        if (libraryManager.GetItemById(libraryId) is not Folder folder)
        {
            logger.Warn($"Library {libraryId:N} did not resolve to a folder - skipping it in the counts.");
            return (0, 0, 0);
        }

        int movies = 0;
        int series = 0;
        int episodes = 0;

        foreach (var child in folder.GetRecursiveChildren())
        {
            // Excludes episodes Jellyfin knows about but has no file for (missing/unaired).
            if (child.IsVirtualItem)
            {
                continue;
            }

            switch (child)
            {
                case Movie:
                    movies++;
                    break;
                case Series:
                    series++;
                    break;
                case Episode:
                    episodes++;
                    break;
            }
        }

        logger.Debug($"Library '{folder.Name}' ({libraryId:N}) - Movies: {movies}, Series: {series}, Episodes: {episodes}");

        return (movies, series, episodes);
    }

    private static Guid[] ParseIds(IEnumerable<string>? libraryIds)
    {
        var parsedIds = new List<Guid>();

        foreach (var rawId in libraryIds ?? Enumerable.Empty<string>())
        {
            // Skipped rather than thrown on: one malformed entry should not wipe out the
            // counts for the whole configuration. TryParse already rejects null and blanks.
            if (Guid.TryParse(rawId, out var parsed))
            {
                parsedIds.Add(parsed);
            }
        }

        return parsedIds.ToArray();
    }

    private static string BuildCacheKey(Guid[] movieLibraries, Guid[] seriesLibraries)
    {
        // Sorted so that the same selection in a different order hits the same entry.
        string movies = string.Join(",", movieLibraries.Select(id => id.ToString("N", CultureInfo.InvariantCulture)).OrderBy(id => id, StringComparer.Ordinal));
        string series = string.Join(",", seriesLibraries.Select(id => id.ToString("N", CultureInfo.InvariantCulture)).OrderBy(id => id, StringComparer.Ordinal));

        return $"{movies}|{series}";
    }

    /// <summary>
    /// The per-library media counts for one newsletter configuration's library selection.
    /// </summary>
    /// <remarks>
    /// Nested here because <see cref="LibraryStats"/> is the only thing that produces one; it is
    /// the shape of a counting result rather than a domain model in its own right. Internal
    /// because nothing outside this plugin consumes it.
    /// </remarks>
    internal sealed class Snapshot
    {
        /// <summary>
        /// Gets the counts for each selected movie library.
        /// </summary>
        public Collection<StoredLibraryCount> MovieLibraries { get; } = new Collection<StoredLibraryCount>();

        /// <summary>
        /// Gets the counts for each selected series library.
        /// </summary>
        public Collection<StoredLibraryCount> SeriesLibraries { get; } = new Collection<StoredLibraryCount>();

        /// <summary>
        /// Gets the combined totals across every selected library.
        /// </summary>
        public LibraryCounts Totals => new LibraryCounts(
            MovieLibraries.Sum(l => l.Titles),
            SeriesLibraries.Sum(l => l.Titles),
            SeriesLibraries.Sum(l => l.Episodes));

        /// <summary>
        /// Gets the change between a stored baseline and this snapshot.
        /// </summary>
        /// <remarks>
        /// Only libraries present on both sides are compared. A library added to the selection
        /// since the baseline was taken has nothing to compare against, and a library removed from
        /// the selection is no longer in this snapshot - so neither can pass a change in scope off
        /// as a change in the library. The totals still reflect the full current selection; only
        /// the change is restricted.
        /// </remarks>
        /// <param name="previousMovieLibraries">The stored per-library movie counts.</param>
        /// <param name="previousSeriesLibraries">The stored per-library series and episode counts.</param>
        /// <returns>The change, as movie, series and episode deltas.</returns>
        public LibraryCounts DeltaFrom(
            Collection<StoredLibraryCount>? previousMovieLibraries,
            Collection<StoredLibraryCount>? previousSeriesLibraries)
        {
            int movies = 0;
            int series = 0;
            int episodes = 0;

            foreach (var current in MovieLibraries)
            {
                var previous = Find(previousMovieLibraries, current.LibraryId);
                if (previous is not null)
                {
                    movies += current.Titles - previous.Titles;
                }
            }

            foreach (var current in SeriesLibraries)
            {
                var previous = Find(previousSeriesLibraries, current.LibraryId);
                if (previous is not null)
                {
                    series += current.Titles - previous.Titles;
                    episodes += current.Episodes - previous.Episodes;
                }
            }

            return new LibraryCounts(movies, series, episodes);
        }

        private static StoredLibraryCount? Find(Collection<StoredLibraryCount>? stored, string libraryId)
        {
            return stored?.FirstOrDefault(l => string.Equals(l.LibraryId, libraryId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class CacheEntry
    {
        public CacheEntry(DateTime capturedAt, Snapshot snapshot)
        {
            CapturedAt = capturedAt;
            Snapshot = snapshot;
        }

        public DateTime CapturedAt { get; }

        public Snapshot Snapshot { get; }
    }
}
