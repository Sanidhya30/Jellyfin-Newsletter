using System.Collections.Generic;
using System.Globalization;
using SQLitePCL.pretty;

namespace Jellyfin.Plugin.Newsletters.Shared.Entities;

/// <summary>
/// Represents a JSON file object containing metadata for a newsletter item.
/// </summary>
public class JsonFileObj
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonFileObj"/> class.
    /// </summary>
    public JsonFileObj()
    {
        Filename = string.Empty;
        Title = string.Empty;
        Season = 0;
        Episode = 0;
        SeriesOverview = string.Empty;
        ImageURL = string.Empty;
        ItemID = string.Empty;
        PosterPath = string.Empty;
        Type = string.Empty;
        PremiereYear = string.Empty;
        RunTime = 0;
        OfficialRating = string.Empty;
        CommunityRating = 0.0f;
        EventType = string.Empty;
        ExternalIds = new Dictionary<string, string>();
        LibraryId = string.Empty;
    }

    /// <summary>
    /// Gets or sets the filename of the item.
    /// </summary>
    public string Filename { get; set; }

    /// <summary>
    /// Gets or sets the title of the item.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the season of the item.
    /// </summary>
    public int Season { get; set; }

    /// <summary>
    /// Gets or sets the episode of the item.
    /// </summary>
    public int Episode { get; set; }

    /// <summary>
    /// Gets or sets the description of the item.
    /// </summary>
    public string SeriesOverview { get; set; }

    /// <summary>
    /// Gets or sets the poster image url of the item.
    /// </summary>
    public string ImageURL { get; set; }

    /// <summary>
    /// Gets or sets the itemId of the item.
    /// </summary>
    public string ItemID { get; set; }

    /// <summary>
    /// Gets or sets the poster path of the item.
    /// </summary>
    public string PosterPath { get; set; }

    /// <summary>
    /// Gets or sets the type(movie/series) of the item.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the premiere year of the item.
    /// </summary>
    public string PremiereYear { get; set; }

    /// <summary>
    /// Gets or sets the run time of the item.
    /// </summary>
    public int RunTime { get; set; }

    /// <summary>
    /// Gets or sets the PG rating of the item.
    /// </summary>
    public string OfficialRating { get; set; }

    /// <summary>
    /// Gets or sets the rating of the item.
    /// </summary>
    public float? CommunityRating { get; set; }

    /// <summary>
    /// Gets or sets the event type of the item (Add/Delete).
    /// </summary>
    public string EventType { get; set; }

    /// <summary>
    /// Gets or sets the library Id of the item.
    /// </summary>
    public string LibraryId { get; set; }

    /// <summary>
    /// Gets dictionary for external IDs like IMDb, TMDb, etc.
    /// </summary>
    public Dictionary<string, string> ExternalIds { get; }

    /// <summary>
    /// Converts a database row to a <see cref="JsonFileObj"/> instance.
    /// </summary>
    /// <param name="row">The database row as a list of <see cref="ResultSetValue"/>.</param>
    /// <returns>A <see cref="JsonFileObj"/> populated with values from the row.</returns>
    public static JsonFileObj ConvertToObj(IReadOnlyList<ResultSetValue> row)
    {
        JsonFileObj obj = new()
        {
            Filename = row[0].ToString(),
            Title = row[1].ToString(),
            Season = int.Parse(row[2].ToString(), CultureInfo.CurrentCulture),
            Episode = int.Parse(row[3].ToString(), CultureInfo.CurrentCulture),
            SeriesOverview = row[4].ToString(),
            ImageURL = row[5].ToString(),
            ItemID = row[6].ToString(),
            PosterPath = row[7].ToString(),
            Type = row[8].ToString(),
            PremiereYear = row[9].ToString(),
            RunTime = string.IsNullOrEmpty(row[10].ToString()) ? 0 : int.Parse(row[10].ToString(), CultureInfo.CurrentCulture),
            OfficialRating = row[11].ToString(),
            CommunityRating = string.IsNullOrEmpty(row[12].ToString()) ? 0.0f : float.Parse(row[12].ToString(), CultureInfo.InvariantCulture),
            EventType = row[13].ToString(),
            LibraryId = row[14].ToString()
        };

        return obj;
    }

    /// <summary>
    /// Returns a dictionary mapping template keys to the corresponding property values of this object.
    /// </summary>
    /// <returns>A dictionary with template keys and their corresponding values from this object.</returns>
    public Dictionary<string, object?> GetReplaceDict()
    {
        Dictionary<string, object?> item_dict = new Dictionary<string, object?>();
        item_dict.Add("{Filename}", this.Filename);
        item_dict.Add("{Title}", this.Title);
        item_dict.Add("{Season}", this.Season);
        item_dict.Add("{Episode}", this.Episode);
        item_dict.Add("{SeriesOverview}", this.SeriesOverview);
        item_dict.Add("{ImageURL}", this.ImageURL);
        item_dict.Add("{ItemID}", this.ItemID);
        item_dict.Add("{PosterPath}", this.PosterPath);
        item_dict.Add("{Type}", this.Type);
        item_dict.Add("{PremiereYear}", this.PremiereYear);
        item_dict.Add("{RunTime}", this.RunTime);
        item_dict.Add("{OfficialRating}", this.OfficialRating);
        item_dict.Add("{CommunityRating}", this.CommunityRating);
        item_dict.Add("{EventType}", this.EventType);
        item_dict.Add("{LibraryId}", this.LibraryId);

        return item_dict;
    }

    /// <summary>
    /// Returns a test instance of <see cref="JsonFileObj"/> with sample data.
    /// </summary>
    /// <returns>A <see cref="JsonFileObj"/> populated with test values.</returns>
    public static JsonFileObj GetTestObj()
    {
        JsonFileObj obj = new()
        {
            Filename = "/data/series/Newsletter/Newsletter-test.mkv",
            Title = "Newsletter-Test",
            Season = 1,
            Episode = 1,
            SeriesOverview = "Newsletter Lorem ipsum dolor sit amet, consectetur adipiscing elit. Vestibulum sit amet feugiat lectus. Mauris eu commodo arcu. Cras auctor ipsum nec sem vestibulum pellentesque.",
            ImageURL = "https://raw.githubusercontent.com/Sanidhya30/Jellyfin-Newsletter/refs/heads/master/images/logo.png",
            ItemID = "0123456789",
            PosterPath = "/data/series/Newsletter/test.jpg",
            Type = "Series",
            PremiereYear = "2025",
            RunTime = 60,
            OfficialRating = "TV-14",
            CommunityRating = 8.413f
        };

        return obj;
    }
}
