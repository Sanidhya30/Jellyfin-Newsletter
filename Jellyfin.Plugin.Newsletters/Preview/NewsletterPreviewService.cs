using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Newsletters.Shared;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Newsletters.Preview;

/// <summary>
/// Reads and edits the queue of items awaiting the next newsletter.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="NewsletterPreviewService"/> class.
/// </remarks>
/// <param name="loggerInstance">The logger instance.</param>
/// <param name="dbInstance">The database instance.</param>
/// <param name="libraryManagerInstance">The library manager instance.</param>
public class NewsletterPreviewService(
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManagerInstance)
{
    private static readonly Dictionary<string, int> EventTypeOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        { "add", 0 }, { "update", 1 }, { "delete", 2 }
    };

    private readonly Logger logger = loggerInstance;
    private readonly SQLiteDatabase db = dbInstance;
    private readonly ILibraryManager libraryManager = libraryManagerInstance;

    /// <summary>
    /// Gets everything currently queued for the next newsletter, grouped by title and event type.
    /// Excluded items are included in the result, flagged, so the UI can show and undo them.
    /// </summary>
    /// <returns>The grouped preview of the next newsletter.</returns>
    public PreviewResponse GetPending()
    {
        var libraryNameMap = LibraryNames.BuildMap(libraryManager, logger);
        var groups = new Dictionary<string, PreviewGroup>(StringComparer.Ordinal);
        var items = new Dictionary<string, List<PreviewItem>>(StringComparer.Ordinal);

        try
        {
            db.CreateConnection();

            const string Sql = "SELECT Filename, Title, Season, Episode, ImageURL, Type, " +
                               "PremiereYear, EventType, LibraryId, Excluded FROM CurrNewsletterData;";

            foreach (var row in db.Query(Sql))
            {
                if (row is null)
                {
                    continue;
                }

                string title = row[1].ToString();
                string eventType = string.IsNullOrEmpty(row[7].ToString()) ? "Add" : row[7].ToString();
                string key = title + " " + eventType;

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new PreviewGroup
                    {
                        Title = title,
                        EventType = eventType,
                        Type = row[5].ToString(),
                        LibraryName = LibraryNames.Resolve(row[8].ToString(), libraryNameMap),
                        PremiereYear = row[6].ToString(),
                        ImageURL = row[4].ToString()
                    };

                    groups[key] = group;
                    items[key] = new List<PreviewItem>();
                }

                if (string.IsNullOrEmpty(group.ImageURL))
                {
                    group.ImageURL = row[4].ToString();
                }

                items[key].Add(new PreviewItem
                {
                    Filename = row[0].ToString(),
                    Season = ParseInt(row[2].ToString()),
                    Episode = ParseInt(row[3].ToString()),
                    Excluded = ParseInt(row[9].ToString()) == 1
                });
            }
        }
        catch (Exception e)
        {
            logger.Error("An error has occured: " + e);
        }
        finally
        {
            db.CloseConnection();
        }

        foreach (var (key, group) in groups)
        {
            var groupItems = items[key]
                .OrderBy(i => i.Season)
                .ThenBy(i => i.Episode)
                .ToList();

            group.Items = groupItems;
            group.TotalCount = groupItems.Count;
            group.ExcludedCount = groupItems.Count(i => i.Excluded);
            group.SeasonSummary = BuildSeasonSummary(group.Type, groupItems);
        }

        var ordered = groups.Values
            .OrderBy(g => EventTypeOrder.GetValueOrDefault(g.EventType ?? "add", 0))
            .ThenBy(g => g.Type == "Movie" ? 0 : 1)
            .ThenBy(g => g.LibraryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PreviewResponse
        {
            Groups = ordered,
            TotalItems = ordered.Sum(g => g.TotalCount),
            ExcludedItems = ordered.Sum(g => g.ExcludedCount),
            LastPublishedDate = Plugin.Instance!.Configuration.LastPublishedDate
        };
    }

    /// <summary>
    /// Excludes the given queued files from the next newsletter, or puts them back.
    /// </summary>
    /// <param name="filenames">The filenames to act on.</param>
    /// <param name="excluded">True to exclude, false to re-include.</param>
    /// <returns>The number of filenames acted on.</returns>
    public int SetExcluded(IReadOnlyList<string> filenames, bool excluded)
    {
        if (filenames is null || filenames.Count == 0)
        {
            return 0;
        }

        try
        {
            db.CreateConnection();

            string list = string.Join(",", filenames.Select(Sanitize));
            db.ExecuteSQL($"UPDATE CurrNewsletterData SET Excluded={(excluded ? 1 : 0)} WHERE Filename IN ({list});");

            logger.Info($"{(excluded ? "Excluded" : "Re-included")} {filenames.Count} item(s) for the next newsletter");
            return filenames.Count;
        }
        catch (Exception e)
        {
            logger.Error("An error has occured: " + e);
            return 0;
        }
        finally
        {
            db.CloseConnection();
        }
    }

    /// <summary>
    /// Summarises the seasons and episodes a group will actually send.
    /// </summary>
    /// <remarks>
    /// Movies carry no season or episode, but the scanner stores 0 rather than a sentinel
    /// (<see cref="Shared.Entities.JsonFileObj"/> initialises both to 0), so the media type is the
    /// only reliable discriminator - season 0 is legitimately "Specials" for a series.
    /// Excluded items are left out so the summary describes what will be sent, falling back to the
    /// full set when everything is excluded so a struck-through card still says what it covers.
    /// </remarks>
    private static string BuildSeasonSummary(string type, List<PreviewItem> groupItems)
    {
        if (string.Equals(type, "Movie", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var included = groupItems.Where(i => !i.Excluded).ToList();
        var describing = included.Count > 0 ? included : groupItems;

        return SeasonSummary.Format(describing.Select(i => (i.Season, i.Episode)).ToList());
    }

    private static string Sanitize(string value)
    {
        return "'" + (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : -1;
    }
}
