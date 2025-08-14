using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.Newsletters.Scanner;

/// <summary>
/// Provides methods for scanning and scraping media items for the Jellyfin Newsletters plugin.
/// </summary>
public class Scraper
{
    // Global Vars
    // Readonly
    private readonly PluginConfiguration config;
    // private readonly string currRunScanList;
    // private readonly string archiveFile;
    // private readonly string currNewsletterDataFile;

    // Non-readonly
    private readonly PosterImageHandler imageHandler;
    private readonly SQLiteDatabase db;
    private readonly Logger logger;
    private int totalLibCount;

    // private List<JsonFileObj> archiveObj;

    /// <summary>
    /// Initializes a new instance of the <see cref="Scraper"/> class.
    /// </summary>
    /// <param name="loggerInstance">The logger instance to use for logging.</param>
    /// <param name="dbInstance">The SQLite database instance to use for data storage.</param>
    /// <param name="imageHandlerInstance">The poster image handler instance to use for image processing.</param>
    public Scraper(Logger loggerInstance, SQLiteDatabase dbInstance, PosterImageHandler imageHandlerInstance)
    {
        logger = loggerInstance;
        db = dbInstance;
        
        config = Plugin.Instance!.Configuration;

        totalLibCount = 0;

        imageHandler = imageHandlerInstance;

        logger.Debug("Setting Config Paths: ");
        logger.Debug("\n  DataPath: " + config.DataPath +
                     "\n  TempDirectory: " + config.TempDirectory +
                     "\n  PluginsPath: " + config.PluginsPath +
                     "\n  NewsletterDir: " + config.NewsletterDir +
                     "\n  ProgramDataPath: " + config.ProgramDataPath +
                     "\n  ProgramSystemPath: " + config.ProgramSystemPath +
                     "\n  SystemConfigurationFilePath: " + config.SystemConfigurationFilePath +
                     "\n  LogDirectoryPath: " + config.LogDirectoryPath );
    }

    /// <summary>
    /// Scans the provided media items and processes them for newsletter data.
    /// </summary>
    /// <param name="items">The list of media items to scan.</param>
    /// <returns>A completed task when the operation is finished.</returns>
    public Task GetSeriesData(IReadOnlyCollection<BaseItem> items)
    {
        logger.Info("Gathering Data...");
        try
        {
            db.CreateConnection();
            BuildJsonObjsToCurrScanfile(items);
            CopyCurrRunDataToNewsletterData();
        }
        catch (Exception e)
        {
            logger.Error("An error has occured: " + e);
        }
        finally
        {
            db.CloseConnection();
        }

        return Task.CompletedTask;
    }

    private void BuildJsonObjsToCurrScanfile(IReadOnlyCollection<BaseItem> items)
    {
        if (!config.SeriesEnabled && !config.MoviesEnabled)
        {
            logger.Info("No Libraries Enabled In Config!");
        }

        // Filter items by type and process accordingly
        var episodeItems = items.Where(item => item is Episode).ToList();
        var movieItems = items.Where(item => item is Movie).ToList();

        if (episodeItems.Count != 0)
        {
            BuildObjs(episodeItems, "Series"); // populate series
        }

        if (movieItems.Count != 0)
        {
            BuildObjs(movieItems, "Movie"); // populate movies
        }
    }

    /// <summary>
    /// Builds and processes objects from the provided media items and adds them to the current run data.
    /// </summary>
    /// <param name="items">The collection of media items to process.</param>
    /// <param name="type">The type of media items ("Series" or "Movie").</param>
    public void BuildObjs(IReadOnlyCollection<BaseItem> items, string type)
    {
        logger.Info($"Parsing {type}..");
        BaseItem episode, season, series;
        totalLibCount = items.Count;
        logger.Info($"Scan Size: {totalLibCount}");
        logger.Info($"Scanning '{type}'");

        var allowedExternalIds = new Dictionary<string, string>
        {
            { "Imdb", "imdb_id" },
            { "Tmdb", "tmdb" },
            { "Tvdb", "tvdb_id" },
        };

        foreach (BaseItem item in items)
        {
            logger.Debug("---------------");
            if (item is not null)
            {
                try
                {
                    if (type == "Series")
                    {
                        episode = item;
                        season = item.GetParent();
                        series = item.GetParent().GetParent();
                    }
                    else if (type == "Movie")
                    {
                        episode = season = series = item;
                    }
                    else
                    {
                        logger.Error("Something went wrong..");
                        continue;
                    }

                    logger.Debug($"ItemId: " + series.Id.ToString("N")); // series ItemId
                    logger.Debug($"{type}: {series.Name}"); // Title
                    logger.Debug($"LocationType: " + episode.LocationType.ToString());
                    if (episode.LocationType.ToString() == "Virtual")
                    {
                        logger.Debug($"No physical path.. Skipping...");
                        continue;
                    }

                    logger.Debug($"Season: {season.Name}"); // Season Name
                    logger.Debug($"Episode Name: {episode.Name}"); // episode Name
                    logger.Debug($"Episode Number: {episode.IndexNumber}"); // episode Name
                    logger.Debug($"Overview: {series.Overview}"); // series overview
                    logger.Debug($"ImageInfo: {series.PrimaryImagePath}");
                    logger.Debug($"Filepath: " + episode.Path); // Filepath, episode.Path is cleaner, but may be empty

                    // NEW PARAMS
                    logger.Debug($"PremiereDate: {series.PremiereDate}"); // series PremiereDate
                    logger.Debug($"OfficialRating: " + series.OfficialRating); // TV-14, TV-PG, etc
                    // logger.Info($"CriticRating: " + series.CriticRating);
                    // logger.Info($"CustomRating: " + series.CustomRating);
                    logger.Debug($"CommunityRating: " + series.CommunityRating); // 8.5, 9.2, etc
                    logger.Debug($"RunTime: " + (int)((float)episode.RunTimeTicks! / 10000 / 60000) + " minutes");

                    foreach (var kvp in series.ProviderIds)
                    {
                        if (allowedExternalIds.TryGetValue(kvp.Key, out var mappedKey))
                        {
                            logger.Debug($"External ID: {allowedExternalIds[kvp.Key]} => {kvp.Value}");
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.Error("Error processing item..");
                    logger.Error(e);
                    continue;
                }

                JsonFileObj currFileObj = new JsonFileObj();

                foreach (var kvp in series.ProviderIds)
                {
                    if (allowedExternalIds.TryGetValue(kvp.Key, out var mappedKey))
                    {
                        // logger.Debug($"External ID: {kvp.Key} => {kvp.Value}");
                        currFileObj.ExternalIds[allowedExternalIds[kvp.Key]] = kvp.Value;
                    }                    
                }

                currFileObj.Filename = episode.Path;
                currFileObj.Title = series.Name;
                currFileObj.Type = type;

                if (series.PremiereDate is not null)
                {
                    // currFileObj.PremiereYear = series.PremiereDate.ToString()!.Split(' ')[0].Split('/')[2]; // NEW {PremierYear}
                    try 
                    {
                        currFileObj.PremiereYear = (series.PremiereDate?.Year ?? 0).ToString(CultureInfo.InvariantCulture);
                        logger.Debug($"PremiereYear: {currFileObj.PremiereYear}");
                    }
                    catch (Exception e)
                    {
                        logger.Warn($"Encountered an error parsing PremiereYear for: {currFileObj.Filename}");
                        logger.Debug(e);
                        currFileObj.PremiereYear = "0"; // Set to 0 if parsing fails
                    }
                }

                currFileObj.RunTime = (int)((float)episode.RunTimeTicks / 10000 / 60000);
                currFileObj.OfficialRating = series.OfficialRating;
                currFileObj.CommunityRating = series.CommunityRating;
                currFileObj.ItemID = series.Id.ToString("N");
                
                if (episode.IndexNumber is int && episode.IndexNumber is not null)
                {
                    currFileObj.Episode = (int)episode.IndexNumber;
                }

                if (type == "Series")
                {
                    if (season.IndexNumber.HasValue)
                    {
                        logger.Debug("Parsing Season Number from IndexNumber");
                        currFileObj.Season = season.IndexNumber.Value;
                    }
                    else
                    {
                        try
                        {
                            logger.Debug("Parsing Season Number from name");
                            currFileObj.Season = int.Parse(season.Name.Split(' ')[1], CultureInfo.CurrentCulture);
                        }
                        catch (Exception e)
                        {
                            logger.Warn($"Encountered an error parsing Season Number for: {currFileObj.Filename}");
                            logger.Debug(e);
                            logger.Warn("Setting Season number to 0 (SPECIALS)");
                            currFileObj.Season = 0;
                        }
                    }
                }
                else if (type == "Movie")
                {
                    currFileObj.Season = 0;
                }

                if (!InDatabase("CurrRunData", currFileObj.Filename.Replace("'", "''", StringComparison.Ordinal), currFileObj.Title.Replace("'", "''", StringComparison.Ordinal), currFileObj.Season, currFileObj.Episode) && 
                    !InDatabase("CurrNewsletterData", currFileObj.Filename.Replace("'", "''", StringComparison.Ordinal), currFileObj.Title.Replace("'", "''", StringComparison.Ordinal), currFileObj.Season, currFileObj.Episode) && 
                    !InDatabase("ArchiveData", currFileObj.Filename.Replace("'", "''", StringComparison.Ordinal), currFileObj.Title.Replace("'", "''", StringComparison.Ordinal), currFileObj.Season, currFileObj.Episode))
                {
                    try
                    {
                        currFileObj.SeriesOverview = series.Overview;

                        logger.Debug("Checking if Primary Image Exists for series");
                        if (series.PrimaryImagePath != null)
                        {
                            logger.Debug("Primary Image series found!");
                            currFileObj.PosterPath = series.PrimaryImagePath;
                        }
                        else if (episode.PrimaryImagePath != null)
                        {
                            logger.Debug("Primary Image series not found. Pulling from Episode");
                            currFileObj.PosterPath = episode.PrimaryImagePath;
                        }
                        else
                        {
                            logger.Warn("Primary Poster not found..");
                            logger.Warn("This may be due to filesystem not being formatted properly.");
                            logger.Warn($"Make sure {currFileObj.Filename} follows the correct formatting below:");
                            logger.Warn(".../MyLibraryName/Series_Name/Season#_or_Specials/Episode.{ext}");
                        }

                        logger.Debug("Checking if PosterPath Exists");
                        if ((currFileObj.PosterPath != null) && (currFileObj.PosterPath.Length > 0))
                        {
                            string url = SetImageURL(currFileObj);

                            if ((url == "429") || (url == "ERR") || string.IsNullOrEmpty(url))
                            {
                                logger.Debug("URL is not attainable at this time. Stopping scan.. Will resume during next scan.");
                                logger.Debug("Not processing current file: " + currFileObj.Filename);
                                break;
                            }

                            currFileObj.ImageURL = url;
                        }
                    }
                    catch (Exception e)
                    {
                        logger.Error($"Encountered an error parsing: {currFileObj.Filename}");
                        logger.Error(e);
                    }
                    finally
                    {
                        // save to "database" : Table currRunScanList
                        logger.Debug("Adding to CurrRunData DB...");
                        currFileObj = NoNull(currFileObj);
                        db.ExecuteSQL("INSERT INTO CurrRunData (Filename, Title, Season, Episode, SeriesOverview, ImageURL, ItemID, PosterPath, Type, PremiereYear, RunTime, OfficialRating, CommunityRating) " +
                                "VALUES (" +
                                    SanitizeDbItem(currFileObj.Filename) +
                                    "," + SanitizeDbItem(currFileObj!.Title) +
                                    "," + ((currFileObj?.Season is null) ? -1 : currFileObj.Season) +
                                    "," + ((currFileObj?.Episode is null) ? -1 : currFileObj.Episode) +
                                    "," + SanitizeDbItem(currFileObj!.SeriesOverview) +
                                    "," + SanitizeDbItem(currFileObj!.ImageURL) +
                                    "," + SanitizeDbItem(currFileObj.ItemID) +
                                    "," + SanitizeDbItem(currFileObj!.PosterPath) +
                                    "," + SanitizeDbItem(currFileObj.Type) +
                                    "," + SanitizeDbItem(currFileObj!.PremiereYear) + 
                                    "," + ((currFileObj?.RunTime is null) ? -1 : currFileObj.RunTime) +
                                    "," + SanitizeDbItem(currFileObj!.OfficialRating) +
                                    "," + ((currFileObj?.CommunityRating is null) ? -1 : currFileObj.CommunityRating) +
                                ");");
                        logger.Debug("Complete!");
                    }
                }
                else
                {
                    logger.Debug("\"" + currFileObj.Filename + "\" has already been processed either by Previous or Current Newsletter!");
                }
            }
        }
    }

    private static JsonFileObj NoNull(JsonFileObj currFileObj)
    {
        currFileObj.Filename ??= string.Empty;

        currFileObj.Title ??= string.Empty;

        currFileObj.SeriesOverview ??= string.Empty;

        currFileObj.ImageURL ??= string.Empty;

        currFileObj.ItemID ??= string.Empty;

        currFileObj.PosterPath ??= string.Empty;

        return currFileObj;
    }

    // Check the filename or the Title of the item in the database, we can't just rely on either the filename or the title,
    // because during the media upgrade the filename might change and during the metadata refresh the title might change.
    // itemId is not used here because it is not reliable, as it can change if the item is upgraded.
    // In case of series we check the season and episode number as well.
    // There are cases in which file names might change due to upgrade of the library.
    private bool InDatabase(string tableName, string fileName, string title, int season = 0, int episode = 0)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(tableName))
        {
            return false;
        }

        foreach (var row in db.Query("SELECT COUNT(*) FROM " + tableName + " WHERE (Filename='" + fileName + "' OR Title='" + title + "') AND Season=" + season + " AND Episode=" + episode + ";"))
        {
            if (row is not null)
            {
                if (int.Parse(row[0].ToString(), CultureInfo.CurrentCulture) > 0)
                {
                    logger.Debug(tableName + " Size: " + row[0].ToString());
                    return true;
                }
            }
        }

        return false;
    }

    private string SetImageURL(JsonFileObj currObj)
    {
        JsonFileObj fileObj;
        string currTitle = currObj.Title.Replace("'", "''", StringComparison.Ordinal);

        // check if URL for series already exists CurrRunData table
        foreach (var row in db.Query("SELECT * FROM CurrRunData;"))
        {
            if (row is not null)
            {
                fileObj = JsonFileObj.ConvertToObj(row);
                if ((fileObj is not null) && (fileObj.Title == currTitle) && (fileObj.ImageURL.Length > 0))
                {
                    logger.Debug("Found Current Scan of URL for " + currTitle + " :: " + fileObj.ImageURL);
                    return fileObj.ImageURL;
                }
            }
        }

        // check if URL for series already exists CurrNewsletterData table
        logger.Debug("Checking if exists in CurrNewsletterData");
        foreach (var row in db.Query("SELECT * FROM CurrNewsletterData;"))
        {
            if (row is not null)
            {
                fileObj = JsonFileObj.ConvertToObj(row);
                if ((fileObj is not null) && (fileObj.Title == currTitle) && (fileObj.ImageURL.Length > 0))
                {
                    logger.Debug("Found Current Scan of URL for " + currTitle + " :: " + fileObj.ImageURL);
                    return fileObj.ImageURL;
                }
            }
        }

        // check if URL for series already exists ArchiveData table
        foreach (var row in db.Query("SELECT * FROM ArchiveData;"))
        {
            if (row is not null)
            {
                fileObj = JsonFileObj.ConvertToObj(row);
                if ((fileObj is not null) && (fileObj.Title == currTitle) && (fileObj.ImageURL.Length > 0))
                {
                    logger.Debug("Found Current Scan of URL for " + currTitle + " :: " + fileObj.ImageURL);
                    return fileObj.ImageURL;
                }
            }
        }

        logger.Debug("Grabbing poster...");
        logger.Debug(currObj.ItemID);
        logger.Debug(currObj.PosterPath);
        // return string.Empty;
        return imageHandler.FetchImagePoster(currObj);
    }

    private void CopyCurrRunDataToNewsletterData()
    {
        // -> copy CurrData Table to NewsletterDataTable
        // -> clear CurrData table
        db.ExecuteSQL("INSERT INTO CurrNewsletterData SELECT * FROM CurrRunData;");
        db.ExecuteSQL("DELETE FROM CurrRunData;");
    }

    private static string SanitizeDbItem(string unsanitized_string)
    {
        // string sanitize_string = string.Empty;
        if (unsanitized_string is null)
        {
            unsanitized_string = string.Empty;
        }

        return "'" + unsanitized_string.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}