#pragma warning disable 1591, SYSLIB0014, CA1002, CS0162
// using SixLabors.ImageSharp;
// using SixLabors.ImageSharp.Processing;
// using SixLabors.ImageSharp.Formats.Jpeg;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.LOGGER;
using Jellyfin.Plugin.Newsletters.Scripts.ENTITIES;
using Jellyfin.Plugin.Newsletters.Shared.DATA;
using Newtonsoft.Json.Linq;
// using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Newsletters.Scanner.NLImageHandler;

public class PosterImageHandler
{
    // Global Vars
    // Readonly
    private readonly PluginConfiguration config;
    private Logger logger;
    private SQLiteDatabase db;
    private static readonly object RateLimitLock = new object();
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(25);
    private static DateTime lastRequestTime = DateTime.MinValue;

    // Non-readonly
    private List<JsonFileObj> archiveSeriesList;
    // private List<string> fileList;

    public PosterImageHandler()
    {
        logger = new Logger();
        db = new SQLiteDatabase();
        config = Plugin.Instance!.Configuration;

        archiveSeriesList = new List<JsonFileObj>();
    }

    public string FetchImagePoster(JsonFileObj item)
    {
        // string imagePath = item.PosterPath;
        // int targetWidth = 100; // Desired width for the resized image
        // using var image = Image.Load(imagePath); // Load original image

        // // Calculate new height while maintaining aspect ratio
        // int newHeight = image.Height * targetWidth / image.Width;

        // // Resize the image in memory
        // image.Mutate(x => x.Resize(targetWidth, newHeight));

        // using var ms = new MemoryStream();
        // image.Save(ms, new JpegEncoder { Quality = 60 }); // Compress with 60% quality

        // var base64 = Convert.ToBase64String(ms.ToArray());
        // return $"data:image/jpeg;base64, {base64}";

        return item.PosterPath;

        // return "data:image/jpeg;base64, " + Convert.ToBase64String(File.ReadAllBytes(item.PosterPath));

        // string apiKey = "d63d13c187e20a4d436a9fd842e7e39c";
        // const int maxRetries = 5;
        // const int retryDelayMs = 1000;

        // foreach (var kvp in item.ExternalIds)
        // {
        //     var externalIdName = kvp.Key;
        //     var externalIdValue = kvp.Value;

        //     for (int attempt = 1; attempt <= maxRetries; attempt++)
        //     {
        //         WebClient wc = new();

        //         // We can add proxy support here if needed, as tmdb is blocked in some regions.
        //         // Currently we have the retry logic in place to handle rate-limiting and other issues.

        //         // Setup proxy (change address and port as needed)
        //         // WebProxy proxy = new WebProxy("http://192.168.1.63:3128"); 

        //         // If your proxy requires authentication, use:
        //         // proxy.Credentials = new NetworkCredential("proxyUser", "proxyPassword");

        //         // wc.Proxy = proxy;

        //         try
        //         {
        //             string url = string.Empty;

        //             logger.Debug($"Trying to fetch TMDB poster path using {externalIdName} => {externalIdValue}");

        //             if (externalIdName == "tmdb")
        //             {
        //                 if (item.Type == "Series")
        //                 {
        //                     url = $"https://api.themoviedb.org/3/tv/{externalIdValue}?api_key={apiKey}";
        //                 }
        //                 else if (item.Type == "Movie")
        //                 {
        //                     url = $"https://api.themoviedb.org/3/movie/{externalIdValue}?api_key={apiKey}";
        //                 }
        //             }
        //             else
        //             {
        //                 url = $"https://api.themoviedb.org/3/find/{externalIdValue}?external_source={externalIdName}&api_key={apiKey}";
        //             }

        //             // Rate-limiting
        //             lock (RateLimitLock)
        //             {
        //                 var now = DateTime.UtcNow;
        //                 var timeSinceLast = now - lastRequestTime;
        //                 if (timeSinceLast < MinInterval)
        //                 {
        //                     logger.Debug($"Sleeping for {MinInterval - timeSinceLast}");
        //                     Thread.Sleep(MinInterval - timeSinceLast);
        //                 }

        //                 lastRequestTime = DateTime.UtcNow;
        //             }

        //             string response = wc.DownloadString(url);
        //             logger.Debug("TMDB Response: " + response);

        //             JObject json = JObject.Parse(response);
        //             JToken? posterPathToken = null;

        //             if (externalIdName == "tmdb")
        //             {
        //                 posterPathToken = json["poster_path"];
        //             }
        //             else
        //             {
        //                 if (item.Type == "Series")
        //                 {
        //                     posterPathToken = json["tv_results"]?.FirstOrDefault()?["poster_path"];
        //                 }
        //                 else if (item.Type == "Movie")
        //                 {
        //                     posterPathToken = json["movie_results"]?.FirstOrDefault()?["poster_path"];
        //                 }
        //             }

        //             if (posterPathToken != null)
        //             {
        //                 string posterPath = posterPathToken.ToString();
        //                 logger.Debug("TMDB Poster Path: " + posterPath);
        //                 return "https://image.tmdb.org/t/p/original" + posterPath;
        //             }
        //             else
        //             {
        //                 logger.Debug($"TMDB Poster path not found for {externalIdName} => {externalIdValue}");
        //                 break; // Don't retry for 404-like situations
        //             }
        //         }
        //         catch (WebException e)
        //         {
        //             logger.Warn($"[Attempt {attempt}] Failed for {externalIdName} => {externalIdValue}. Status: {e.Status}");

        //             if (attempt == maxRetries)
        //             {
        //                 logger.Debug("Max retry attempts reached for this external ID.");
        //                 logger.Debug("WebClient Return STATUS: " + e.Status);
        //                 logger.Debug(e.ToString().Split(")")[0].Split("(")[1]);
        //             }

        //             Thread.Sleep(retryDelayMs);
        //         }
        //     }

        //     logger.Debug("Trying the next external ID...");
        // }

        // return string.Empty;
    }

    // private string UploadToImgur(string posterFilePath)
    // {
    //     WebClient wc = new();

    //     NameValueCollection values = new()
    //     {
    //         { "image", Convert.ToBase64String(File.ReadAllBytes(posterFilePath)) }
    //     };

    //     wc.Headers.Add("Authorization", "Client-ID " + config.ApiKey);

    //     try
    //     {
    //         byte[] response = wc.UploadValues("https://api.imgur.com/3/upload.xml", values);

    //         string res = System.Text.Encoding.Default.GetString(response);

    //         logger.Debug("Imgur Response: " + res);

    //         logger.Info("Imgur Uploaded! Link:");
    //         logger.Info(res.Split("<link>")[1].Split("</link>")[0]);

    //         return res.Split("<link>")[1].Split("</link>")[0];
    //     }
    //     catch (WebException e)
    //     {
    //         logger.Debug("WebClient Return STATUS: " + e.Status);
    //         logger.Debug(e.ToString().Split(")")[0].Split("(")[1]);
    //         try
    //         {
    //             return e.ToString().Split(")")[0].Split("(")[1];
    //         }
    //         catch (Exception ex)
    //         {
    //             logger.Error("Error caught while trying to parse webException error: " + ex);
    //             return "ERR";
    //         }
    //     }
    // }
}