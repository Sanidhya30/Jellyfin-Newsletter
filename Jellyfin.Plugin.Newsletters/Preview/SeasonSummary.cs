using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.Newsletters.Preview;

/// <summary>
/// Formats the season and episode numbers of a queued group into a short human-readable summary.
/// </summary>
public static class SeasonSummary
{
    /// <summary>
    /// Builds a summary such as "S1 E1-3, S2 E5". Movies, which the scanner stores with
    /// season and episode -1, produce an empty string.
    /// </summary>
    /// <param name="episodes">The season/episode pairs belonging to one queued group.</param>
    /// <returns>The formatted summary, or an empty string when there is nothing to describe.</returns>
    public static string Format(IReadOnlyList<(int Season, int Episode)> episodes)
    {
        if (episodes is null || episodes.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        foreach (var season in episodes.Where(e => e.Season >= 0).GroupBy(e => e.Season).OrderBy(g => g.Key))
        {
            var numbers = season.Select(e => e.Episode)
                                .Where(e => e >= 0)
                                .Distinct()
                                .OrderBy(e => e)
                                .ToList();

            string label = "S" + season.Key.ToString(CultureInfo.InvariantCulture);
            parts.Add(numbers.Count == 0 ? label : label + " E" + FormatRuns(numbers));
        }

        return string.Join(", ", parts);
    }

    private static string FormatRuns(List<int> sorted)
    {
        var runs = new List<string>();
        int start = sorted[0];
        int previous = start;

        for (int i = 1; i <= sorted.Count; i++)
        {
            if (i < sorted.Count && sorted[i] == previous + 1)
            {
                previous = sorted[i];
                continue;
            }

            runs.Add(start == previous
                ? start.ToString(CultureInfo.InvariantCulture)
                : start.ToString(CultureInfo.InvariantCulture) + "-" + previous.ToString(CultureInfo.InvariantCulture));

            if (i < sorted.Count)
            {
                start = previous = sorted[i];
            }
        }

        return string.Join(", ", runs);
    }
}
