#pragma warning disable 1591
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.Newsletters.LOGGER;
using SQLitePCL;
using SQLitePCL.pretty;

namespace Jellyfin.Plugin.Newsletters.Scripts.ENTITIES;

public class JsonFileObj
{
    private Logger? logger;

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
        ExternalIds = new Dictionary<string, string>();
    }

    public string Filename { get; set; }

    public string Title { get; set; }

    // public string Season { get; set; }

    public int Season { get; set; }

    public int Episode { get; set; }

    // public string Episode { get; set; }

    public string SeriesOverview { get; set; }

    public string ImageURL { get; set; }

    public string ItemID { get; set; }

    public string PosterPath { get; set; }

    public string Type { get; set; }

    public string PremiereYear { get; set; }

    public int RunTime { get; set; }

    public string OfficialRating { get; set; }

    public float? CommunityRating { get; set; }

    //Dictionary to store external IDs like IMDb, TMDb, etc.
    public Dictionary<string, string> ExternalIds { get; }

    public JsonFileObj ConvertToObj(IReadOnlyList<ResultSetValue> row)
    {
        // Filename = string.Empty; 0
        // Title = string.Empty; 1
        // Season = 0; 2
        // Episode = 0; 3
        // SeriesOverview = string.Empty; 4
        // ImageURL = string.Empty; 5
        // ItemID = string.Empty; 6
        // PosterPath = string.Empty; 7

        logger = new Logger();
        JsonFileObj obj = new JsonFileObj()
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
            CommunityRating = string.IsNullOrEmpty(row[12].ToString()) ? 0.0f : float.Parse(row[12].ToString(), CultureInfo.CurrentCulture)
        };

        return obj;
    }

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
        item_dict!.Add("{CommunityRating}", this.CommunityRating);

        return item_dict;        
    }

    public JsonFileObj GetTestObj()
    {
        // Filename = string.Empty; 0
        // Title = string.Empty; 1
        // Season = 0; 2
        // Episode = 0; 3
        // SeriesOverview = string.Empty; 4
        // ImageURL = string.Empty; 5
        // ItemID = string.Empty; 6
        // PosterPath = string.Empty; 7

        logger = new Logger();
        JsonFileObj obj = new JsonFileObj()
        {
            Filename = "/data/series/Newsletter/Newsletter-test.mkv",
            Title = "Newsletter-Test",
            Season = 1,
            Episode = 1,
            SeriesOverview = "Newsletter Lorem ipsum dolor sit amet, consectetur adipiscing elit. Vestibulum sit amet feugiat lectus. Mauris eu commodo arcu. Cras auctor ipsum nec sem vestibulum pellentesque. Pellentesque habitant morbi tristique senectus et netus et malesuada fames ac turpis egestas. Maecenas sed augue a enim facilisis suscipit sit amet sit amet odio. Phasellus tincidunt tortor arcu, vel vestibulum ipsum ullamcorper id. Etiam feugiat cursus ultricies. Nullam ornare ultrices pharetra. Phasellus vehicula nisl ex, id accumsan enim dapibus eu. Phasellus ultrices rhoncus metus ut vehicula. Curabitur tincidunt, eros non ullamcorper eleifend, orci sapien ornare nisi, eu tempor mauris lorem eget mi. Morbi nec cursus augue. Phasellus congue, risus non iaculis consequat, neque est ullamcorper enim, vitae commodo mi risus quis turpis. Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
            ImageURL = "https://raw.githubusercontent.com/Sanidhya30/Jellyfin-Newsletter/refs/heads/master/logo.png",
            ItemID = "0123456789",
            PosterPath = "/data/series/Newsletter/test.jpg",
            Type = "Series",
            PremiereYear = "2025",
            RunTime = 60,
            OfficialRating = "TV-14",
            CommunityRating = 10
        };

        return obj;
    }
}