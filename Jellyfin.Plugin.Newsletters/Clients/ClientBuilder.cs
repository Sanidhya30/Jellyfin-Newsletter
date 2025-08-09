using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Jellyfin.Plugin.Newsletters.Clients;

/// <summary>
/// Provides methods for building newsletter clients and processing newsletter data.
/// </summary>
public class ClientBuilder(Logger loggerInstance,
    SQLiteDatabase dbInstance)
{    
    /// <summary>
    /// Gets the plugin configuration instance.
    /// </summary>
    protected PluginConfiguration Config { get; } = Plugin.Instance!.Configuration;

    /// <summary>
    /// Gets the database instance.
    /// </summary>
    protected SQLiteDatabase Db { get; } = dbInstance;

    /// <summary>
    /// Gets the logger.
    /// </summary>
    protected Logger Logger { get; } = loggerInstance;

    /// <summary>
    /// Parses series information from the given JsonFileObj and returns a collection of NlDetailsJson.
    /// </summary>
    /// <param name="currObj">The current JsonFileObj containing series information.</param>
    /// <returns>A collection of NlDetailsJson representing the parsed series details.</returns>
    protected ReadOnlyCollection<NlDetailsJson> ParseSeriesInfo(JsonFileObj currObj)
    {
        List<NlDetailsJson> compiledList = new List<NlDetailsJson>();
        List<NlDetailsJson> finalList = new List<NlDetailsJson>();

        foreach (var row in Db.Query("SELECT * FROM CurrNewsletterData WHERE Title='" + currObj.Title + "';"))
        {
            if (row is not null)
            {
                JsonFileObj itemObj = JsonFileObj.ConvertToObj(row);

                NlDetailsJson tempVar = new()
                {
                    Title = itemObj.Title,
                    Season = itemObj.Season,
                    Episode = itemObj.Episode
                };

                Logger.Debug("tempVar.Season: " + tempVar.Season + " : tempVar.Episode: " + tempVar.Episode);
                compiledList.Add(tempVar);
            }
        }

        List<int> tempEpsList = new List<int>();
        NlDetailsJson currSeriesDetailsObj = new();

        int currSeason = -1;
        bool newSeason = true;
        int list_len = compiledList.Count;
        int count = 1;
        foreach (NlDetailsJson item in SortListBySeason(SortListByEpisode(compiledList)))
        {
            Logger.Debug("After Sort in foreach: Season::" + item.Season + "; Episode::" + item.Episode);
            Logger.Debug("Count/list_len: " + count + "/" + list_len);

            NlDetailsJson CopyJsonFromExisting(NlDetailsJson obj)
            {
                NlDetailsJson newJson = new()
                {
                    Season = obj.Season,
                    EpisodeRange = obj.EpisodeRange
                };
                return newJson;
            }

            void AddNewSeason()
            {
                Logger.Debug("AddNewSeason()");
                currSeriesDetailsObj.Season = currSeason = item.Season;
                newSeason = false;
                tempEpsList.Add(item.Episode);
            }

            void AddCurrentSeason()
            {
                // Logger.Debug("AddCurrentSeason()");
                Logger.Debug("Seasons Match " + currSeason + "::" + item.Season);
                tempEpsList.Add(item.Episode);
            }

            void EndOfSeason()
            {
                // process season, them increment
                Logger.Debug("EndOfSeason()");
                Logger.Debug($"tempEpsList Size: {tempEpsList.Count}");
                if (tempEpsList.Count != 0)
                {
                    Logger.Debug("tempEpsList is populated");
                    tempEpsList.Sort();
                    if (IsIncremental(tempEpsList) && tempEpsList.Count > 1)
                    {
                        currSeriesDetailsObj.EpisodeRange = tempEpsList.First() + " - " + tempEpsList.Last();
                    }
                    else if (tempEpsList.First() == tempEpsList.Last())
                    {
                        currSeriesDetailsObj.EpisodeRange = tempEpsList.First().ToString(System.Globalization.CultureInfo.CurrentCulture);
                    }
                    else
                    {
                        string epList = string.Empty;
                        int firstRangeEp, prevEp;
                        firstRangeEp = prevEp = -1;

                        bool IsNext(int prev, int curr)
                        {
                            Logger.Debug("Checking Prev and Curr..");
                            Logger.Debug($"prev: {prev} :: curr: {curr}");
                            Logger.Debug(prev + 1);
                            if (curr == prev + 1)
                            {
                                return true;
                            }

                            return false;
                        }

                        string ProcessEpString(int firstRangeEp, int prevEp)
                        {
                            if (firstRangeEp == prevEp)
                            {
                                epList += firstRangeEp + ",";
                            }
                            else
                            {
                                epList += firstRangeEp + "-" + prevEp + ",";
                            }

                            return epList;
                        }

                        foreach (int ep in tempEpsList)
                        {
                            Logger.Debug("-------------------");
                            Logger.Debug($"FOREACH firstRangeEp :: prevEp :: ep = {firstRangeEp} :: {prevEp} :: {ep} ");
                            Logger.Debug(ep == tempEpsList.Last());
                            // if first passthrough
                            if (firstRangeEp == -1)
                            {
                                Logger.Debug("First pass of episode list");
                                firstRangeEp = prevEp = ep;
                                continue;
                            }

                            // If incremental
                            if (IsNext(prevEp, ep) && (ep != tempEpsList.Last()))
                            {
                                Logger.Debug("Is Next and Isn't last");
                                prevEp = ep;
                                continue;
                            }
                            else if (IsNext(prevEp, ep) && (ep == tempEpsList.Last()))
                            {
                                Logger.Debug("Is Next and Is last");
                                prevEp = ep;
                                ProcessEpString(firstRangeEp, prevEp);
                            }
                            else if (!IsNext(prevEp, ep) && (ep == tempEpsList.Last()))
                            {
                                Logger.Debug("Isn't Next and Is last");
                                // process previous
                                ProcessEpString(firstRangeEp, prevEp);
                                // process last episode
                                epList += ep;
                                continue;
                            }
                            else
                            {
                                Logger.Debug("Isn't Next and Isn't last");
                                ProcessEpString(firstRangeEp, prevEp);
                                firstRangeEp = prevEp = ep;
                            }
                        }

                        // better numbering here
                        Logger.Debug($"epList: {epList}");
                        currSeriesDetailsObj.EpisodeRange = epList.TrimEnd(',');
                    }

                    Logger.Debug("Adding to finalListObj: " + JsonConvert.SerializeObject(currSeriesDetailsObj));
                    // finalList.Add(currSeriesDetailsObj);
                    finalList.Add(CopyJsonFromExisting(currSeriesDetailsObj));

                    // increment season
                    currSeriesDetailsObj.Season = currSeason = item.Season;
                    currSeriesDetailsObj.EpisodeRange = string.Empty;

                    // currSeason = item.Season;
                    tempEpsList.Clear();
                    newSeason = true;
                }
            }

            Logger.Debug("CurrItem Season/Episode number: " + item.Season + "/" + item.Episode);
            if (newSeason)
            {
                AddNewSeason();
            }
            else if (currSeason == item.Season) // && (count < list_len))
            {
                AddCurrentSeason();
            }
            else if (count < list_len)
            {
                EndOfSeason();
                AddNewSeason();
            }
            else
            {
                EndOfSeason();
            }

            if (count == list_len)
            {
                EndOfSeason();
            }

            count++;
        }

        Logger.Debug("FinalList Length: " + finalList.Count);

        foreach (NlDetailsJson item in finalList)
        {
            Logger.Debug("FinalListObjs: " + JsonConvert.SerializeObject(item));
        }

        return finalList.AsReadOnly();
    }

    /// <summary>
    /// Resizes an image to the specified width and JPEG quality, with retry logic for I/O exceptions.
    /// </summary>
    /// <param name="imagePath">The file path of the image to resize.</param>
    /// <param name="maxRetries">The maximum number of retry attempts for loading the image.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds between retry attempts.</param>
    /// <param name="targetWidth">The target width for the resized image.</param>
    /// <param name="jpegQuality">The JPEG quality for the output image.</param>
    /// <returns>
    /// A tuple containing the resized image as a <see cref="MemoryStream"/>, a unique content ID, and a success flag.
    /// </returns>
    protected (MemoryStream? ResizedStream, string ContentId, bool Success) ResizeImage(string imagePath, int maxRetries = 5, int delayMilliseconds = 200, int targetWidth = 500, int jpegQuality = 80)
    {
        string contentId = $"image_{Guid.NewGuid()}.jpg";
        int attempt = 0;
        MemoryStream? resizedStream = null;
        
        // Sometimes we're getting I/O exceptions when trying to load images, so we retry a few times
        Logger.Debug($"Attempting to resize image: {imagePath}, Target Width: {targetWidth}, JPEG Quality: {jpegQuality}");
        while (attempt < maxRetries)
        {
            try
            {
                using (var image = Image.Load(imagePath))
                {
                    int targetHeight = image.Height * targetWidth / image.Width;

                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(targetWidth, targetHeight)
                    }));

                    resizedStream = new MemoryStream();
                    image.Save(resizedStream, new JpegEncoder { Quality = jpegQuality });
                    resizedStream.Position = 0;

                    return (resizedStream, contentId, true);
                }
            }
            catch (Exception ex)
            {
                attempt++;
                Logger.Warn($"[Attempt {attempt}] Failed to load/process image for {imagePath}: {ex.Message}");

                if (attempt < maxRetries)
                {
                    Thread.Sleep(delayMilliseconds);
                }
            }
        }

        Logger.Error($"Failed to process image for {imagePath} after {maxRetries} attempts.");
        return (null, contentId, false);
    }

    private static bool IsIncremental(List<int> values)
    {
        return values.Skip(1).Select((v, i) => v == (values[i] + 1)).All(v => v);
    }

    private static List<NlDetailsJson> SortListBySeason(List<NlDetailsJson> list)
    {
        return list.OrderBy(x => x.Season).ToList();
    }

    private static List<NlDetailsJson> SortListByEpisode(List<NlDetailsJson> list)
    {
        return list.OrderBy(x => x.Episode).ToList();
    }
}