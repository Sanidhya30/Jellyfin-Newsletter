using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using Jellyfin.Plugin.Newsletters.Shared.Models;
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

        totalLibCount = 0;

        imageHandler = imageHandlerInstance;

        logger.Debug("Setting Config Paths: ");
        logger.Debug("\n  DataPath: " + Config.DataPath +
                     "\n  TempDirectory: " + Config.TempDirectory +
                     "\n  PluginsPath: " + Config.PluginsPath +
                     "\n  NewsletterDir: " + Config.NewsletterDir +
                     "\n  ProgramDataPath: " + Config.ProgramDataPath +
                     "\n  ProgramSystemPath: " + Config.ProgramSystemPath +
                     "\n  SystemConfigurationFilePath: " + Config.SystemConfigurationFilePath +
                     "\n  LogDirectoryPath: " + Config.LogDirectoryPath );
    }

    /// <summary>
    /// Gets the current plugin configuration.
    /// </summary>
    private PluginConfiguration Config => Plugin.Instance!.Configuration;

    /// <summary>
    /// Scans the provided media items and processes them for newsletter data.
    /// </summary>
    /// <param name="items">The list of media items to scan.</param>
    /// <returns>A completed task when the operation is finished.</returns>
    public Task GetSeriesData(IReadOnlyCollection<(BaseItem Item, EventType EventType)> items)
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

    private void BuildJsonObjsToCurrScanfile(IReadOnlyCollection<(BaseItem Item, EventType EventType)> items)
    {
        if (!Config.SeriesEnabled && !Config.MoviesEnabled)
        {
            logger.Info("No Libraries Enabled In Config!");
        }

        // Filter items by type and process accordingly
        var episodeItems = items.Where(item => item.Item is Episode).ToList();
        var movieItems = items.Where(item => item.Item is Movie).ToList();

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
    public void BuildObjs(IReadOnlyCollection<(BaseItem Item, EventType EventType)> items, string type)
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

        foreach (var (item, eventType) in items)
        {
            logger.Debug("---------------");
            if (item is not null)
            {
                try
                {
                    if (type == "Series")
                    {
                        episode = item;

                        if (eventType == EventType.Delete && item.ParentId.Equals(Guid.Empty))
                        {
                            item.ParentId = ((Episode)item).SeasonId;
                        }

                        season = item.GetParent();
                        if (season is null) 
                        { 
                            logger.Debug("No season parent; skipping"); 
                            continue; 
                        }
                        
                        series = season.GetParent();
                        if (series is null) 
                        { 
                            logger.Debug("No series parent; skipping");
                            continue; 
                        }
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

                    logger.Debug("EventType: " + eventType.ToString());
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

                    var runtimeMinutes = episode.RunTimeTicks.HasValue ? (int)(episode.RunTimeTicks.Value / 10000 / 60000) : 0;
                    var communityRating = (series.CommunityRating ?? -1).ToString(CultureInfo.InvariantCulture);
                    logger.Debug($"CommunityRating: " + communityRating); // 8.5, 9.2, etc
                    logger.Debug($"RunTime: " + runtimeMinutes + " minutes");

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

                currFileObj.RunTime = episode.RunTimeTicks.HasValue ? (int)(episode.RunTimeTicks.Value / 10000 / 60000) : 0;
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

                string url = SetImageURL(currFileObj);

                if ((url == "429") || (url == "ERR") || string.IsNullOrEmpty(url))
                {
                    logger.Warn("URL is not attainable at this time. Stopping scan.. Will resume during next scan.");
                    logger.Warn("Setting empty image url: " + currFileObj.Filename);
                    currFileObj.ImageURL = string.Empty;
                }
                else
                {
                    currFileObj.ImageURL = url;
                }

                // Process the item based on the event type
                ProcessItemByEventType(currFileObj, eventType);
            }
        }
    }

    /// <summary>
    /// Processes the item based on its event type (Add, Delete).
    /// </summary>
    /// <param name="item">The item to process.</param>
    /// <param name="eventType">The type of event to process.</param>
    private void ProcessItemByEventType(JsonFileObj item, EventType eventType)
    {
        switch (eventType)
        {
            case EventType.Add:
                ProcessAddEvent(item);
                break;
            case EventType.Delete:
                ProcessDeleteEvent(item);
                break;
        }
    }

    /// <summary>
    /// Processes an Add event for the given item.
    /// </summary>
    /// <param name="item">The item being added.</param>
    private void ProcessAddEvent(JsonFileObj item)
    {
        logger.Debug($"Processing Add event for {item.Title}");
        
        // Check if item was recently deleted (which would make this an update)
        // The logic of update is based on how radarr/sonarr upgrades the media
        // whenever a file is upgraded it delete the old file and add the new one.
        // This is different then the jellyfin in-built update event which is triggered when there is a metadata change.
        string filename = item.Filename.Replace("'", "''", StringComparison.Ordinal);
        string title = item.Title.Replace("'", "''", StringComparison.Ordinal);
        int season = item.Season;
        int episode = item.Episode;

        bool wasDeletedInCurrRunData = InDatabaseWithEventType("CurrRunData", filename, title, season, episode, "Delete");
        bool wasDeletedInNewsletterData = InDatabaseWithEventType("CurrNewsletterData", filename, title, season, episode, "Delete");
        
        if (wasDeletedInCurrRunData || wasDeletedInNewsletterData)
        {
            logger.Debug($"Item {item.Title} was recently deleted - treating as update");
            ProcessUpdateEvent(item, wasDeletedInCurrRunData, wasDeletedInNewsletterData);
            return;
        }

        AddItemToDatabase(item, "Add");
        logger.Debug("Addition entry added to CurrRunData");
    }

    /// <summary>
    /// Processes an Update event for the given item.
    /// </summary>
    /// <param name="item">The item being updated.</param>
    /// <param name="wasDeletedInCurrRunData">Whether the item was previously deleted in CurrRunData.</param>
    /// <param name="wasDeletedInNewsletterData">Whether the item was previously deleted in CurrNewsletterData.</param>
    private void ProcessUpdateEvent(JsonFileObj item, bool wasDeletedInCurrRunData = false, bool wasDeletedInNewsletterData = false)
    {
        logger.Debug($"Processing Update event for {item.Title}");

        string filename = item.Filename.Replace("'", "''", StringComparison.Ordinal);
        string title = item.Title.Replace("'", "''", StringComparison.Ordinal);
        int season = item.Season;
        int episode = item.Episode;

        // If we already know it was deleted in CurrRunData/CurrNewsletterData, update it
        // Everytime we're going to send newsletter for update even if it's already in ArchiveData
        // therefore no need to check if it's already in ArchiveData
        if (wasDeletedInCurrRunData)
        {
            RemoveFromDatabase("CurrRunData", filename, title, season, episode);
        }
        
        if (wasDeletedInNewsletterData)
        {
            RemoveFromDatabase("CurrNewsletterData", filename, title, season, episode);
        }

        AddItemToDatabase(item, "Update");
        logger.Debug("Update entry added to CurrRunData");
    }

    /// <summary>
    /// Processes a Delete event for the given item.
    /// </summary>
    /// <param name="item">The item being deleted.</param>
    private void ProcessDeleteEvent(JsonFileObj item)
    {        
        // Handle the deletion logic
        logger.Info($"Processing deletion for {item.Type}: {item.Title} (S{item.Season}E{item.Episode})");

        string filename = item.Filename.Replace("'", "''", StringComparison.Ordinal);
        string title = item.Title.Replace("'", "''", StringComparison.Ordinal);
        int season = item.Season;
        int episode = item.Episode;

        // Check if there's already event(Add/Update) for this item in either CurrRunData or CurrNewsletterData
        // if there are any remove them from the table as we don't want to send the newsletter for that event
        // (newsletter hasn't been sent yet, so CurrNewsletterData items are still pending)
        bool hasExistingRunData = InDatabase("CurrRunData", filename, title, season, episode);
        bool hasExistingNewsletterData = InDatabase("CurrNewsletterData", filename, title, season, episode);

        if (hasExistingRunData || hasExistingNewsletterData)
        {
            logger.Debug("Found existing Add/Update event for deleted item - removing it since item was never really added");
            if (hasExistingRunData)
            {
                RemoveFromDatabase("CurrRunData", filename, title, season, episode);
            }

            if (hasExistingNewsletterData)
            {
                RemoveFromDatabase("CurrNewsletterData", filename, title, season, episode);
            }
        }

        // Always add deletion entry (even if not previously in archive)
        AddItemToDatabase(item, "Delete");
        logger.Debug("Deletion entry added to CurrRunData");
    }

    /// <summary>
    /// Adds an item to the database with the specified event type.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <param name="eventType">The event type to set.</param>
    private void AddItemToDatabase(JsonFileObj item, string eventType)
    {
        item = NoNull(item);
        db.ExecuteSQL("INSERT INTO CurrRunData (Filename, Title, Season, Episode, SeriesOverview, ImageURL, ItemID, PosterPath, Type, PremiereYear, RunTime, OfficialRating, CommunityRating, EventType) " +
                "VALUES (" +
                    SanitizeDbItem(item.Filename) +
                    "," + SanitizeDbItem(item.Title) +
                    "," + ((item?.Season is null) ? -1 : item.Season) +
                    "," + ((item?.Episode is null) ? -1 : item.Episode) +
                    "," + SanitizeDbItem(item!.SeriesOverview) +
                    "," + SanitizeDbItem(item!.ImageURL) +
                    "," + SanitizeDbItem(item.ItemID) +
                    "," + SanitizeDbItem(item!.PosterPath) +
                    "," + SanitizeDbItem(item.Type) +
                    "," + SanitizeDbItem(item!.PremiereYear) +
                    "," + ((item?.RunTime is null) ? -1 : item.RunTime) +
                    "," + SanitizeDbItem(item!.OfficialRating) +
                    "," + (item.CommunityRating ?? -1).ToString(CultureInfo.InvariantCulture) +
                    "," + SanitizeDbItem(eventType) +
                ");");
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
        // return string.Empty;
        return imageHandler.FetchImagePoster(currObj);
    }

    private void CopyCurrRunDataToNewsletterData()
    {
        // -> copy CurrData Table to NewsletterDataTable
        // -> clear CurrData table
        db.ExecuteSQL("INSERT OR REPLACE INTO CurrNewsletterData SELECT * FROM CurrRunData;");
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

    /// <summary>
    /// Checks if an item exists in the specified database table with a specific EventType.
    /// </summary>
    /// <param name="tableName">The name of the table to check.</param>
    /// <param name="fileName">The filename of the item.</param>
    /// <param name="title">The title of the item.</param>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    /// <param name="eventType">The event type to check for.</param>
    /// <returns>True if the item exists with the specified event type; otherwise, false.</returns>
    private bool InDatabaseWithEventType(string tableName, string fileName, string title, int season, int episode, string eventType)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(tableName))
        {
            return false;
        }

        foreach (var row in db.Query("SELECT COUNT(*) FROM " + tableName + " WHERE (Filename='" + fileName + "' OR Title='" + title + "') AND Season=" + season + " AND Episode=" + episode + " AND EventType='" + eventType + "';"))
        {
            if (row is not null)
            {
                if (int.Parse(row[0].ToString(), CultureInfo.CurrentCulture) > 0)
                {
                    logger.Debug($"Found {eventType} event for item in {tableName}");
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Removes an item from the specified database table.
    /// </summary>
    /// <param name="tableName">The name of the table to remove from.</param>
    /// <param name="fileName">The filename of the item.</param>
    /// <param name="title">The title of the item.</param>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    private void RemoveFromDatabase(string tableName, string fileName, string title, int season, int episode)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(tableName))
        {
            return;
        }

        db.ExecuteSQL("DELETE FROM " + tableName + " WHERE (Filename='" + fileName + "' OR Title='" + title + "') AND Season=" + season + " AND Episode=" + episode + ";");
        logger.Debug($"Removed item from {tableName}");
    }
}
