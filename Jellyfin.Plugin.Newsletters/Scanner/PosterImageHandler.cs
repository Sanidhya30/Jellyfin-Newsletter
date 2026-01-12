using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.Newsletters.Scanner;

/// <summary>
/// Handles fetching and processing of poster images from external sources such as TMDB.
/// </summary>
public class PosterImageHandler(Logger loggerInstance)
{
    // Global Vars
    // Readonly
    private readonly Logger logger = loggerInstance;
    private static readonly object RateLimitLock = new();
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(25);
    private static DateTime lastRequestTime = DateTime.MinValue;

    /// <summary>
    /// Fetches the poster image URL for the given item using external IDs (e.g., TMDB).
    /// </summary>
    /// <param name="item">The item containing external IDs and type information.</param>
    /// <returns>The URL of the poster image if found; otherwise, an empty string.</returns>
    public string FetchImagePoster(JsonFileObj item)
    {
        string apiKey = "d63d13c187e20a4d436a9fd842e7e39c";
        const int maxRetries = 5;
        const int retryDelayMs = 1000;

        foreach (var kvp in item.ExternalIds)
        {
            var externalIdName = kvp.Key;
            var externalIdValue = kvp.Value;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                using var httpClient = new HttpClient();

                // We can add proxy support here if needed, as tmdb is blocked in some regions.
                // Currently we have the retry logic in place to handle rate-limiting and other issues.

                // Setup proxy (change address and port as needed)
                // WebProxy proxy = new WebProxy("http://192.168.1.63:3128"); 

                // If your proxy requires authentication, use:
                // proxy.Credentials = new NetworkCredential("proxyUser", "proxyPassword");

                // wc.Proxy = proxy;

                try
                {
                    string url = string.Empty;

                    logger.Debug($"Trying to fetch TMDB poster path using {externalIdName} => {externalIdValue}");

                    if (externalIdName == "tmdb")
                    {
                        if (item.Type == "Series")
                        {
                            url = $"https://api.themoviedb.org/3/tv/{externalIdValue}?api_key={apiKey}";
                        }
                        else if (item.Type == "Movie")
                        {
                            url = $"https://api.themoviedb.org/3/movie/{externalIdValue}?api_key={apiKey}";
                        }
                    }
                    else
                    {
                        url = $"https://api.themoviedb.org/3/find/{externalIdValue}?external_source={externalIdName}&api_key={apiKey}";
                    }

                    // Rate-limiting
                    lock (RateLimitLock)
                    {
                        var now = DateTime.UtcNow;
                        var timeSinceLast = now - lastRequestTime;
                        if (timeSinceLast < MinInterval)
                        {
                            logger.Debug($"Sleeping for {MinInterval - timeSinceLast}");
                            Thread.Sleep(MinInterval - timeSinceLast);
                        }

                        lastRequestTime = DateTime.UtcNow;
                    }

                    string response = httpClient.GetStringAsync(url).GetAwaiter().GetResult();
                    logger.Debug("TMDB Response: " + response);

                    JObject json = JObject.Parse(response);
                    JToken? posterPathToken = null;

                    if (externalIdName == "tmdb")
                    {
                        posterPathToken = json["poster_path"];
                    }
                    else
                    {
                        if (item.Type == "Series")
                        {
                            posterPathToken = json["tv_results"]?.FirstOrDefault()?["poster_path"];
                        }
                        else if (item.Type == "Movie")
                        {
                            posterPathToken = json["movie_results"]?.FirstOrDefault()?["poster_path"];
                        }
                    }

                    if (posterPathToken != null)
                    {
                        string posterPath = posterPathToken.ToString();
                        logger.Debug("TMDB Poster Path: " + posterPath);
                        return "https://image.tmdb.org/t/p/original" + posterPath;
                    }
                    else
                    {
                        logger.Debug($"TMDB Poster path not found for {externalIdName} => {externalIdValue}");
                        break; // Don't retry for 404-like situations
                    }
                }
                catch (Exception e)
                {
                    // Handle both WebException and HttpRequestException (and any other exceptions)
                    string exceptionType = e.GetType().Name;
                    logger.Warn($"[Attempt {attempt}] {exceptionType} for {externalIdName} => {externalIdValue}. Message: {e.Message}");

                    if (attempt == maxRetries)
                    {
                        logger.Error($"Max retry attempts reached for {externalIdName} => {externalIdValue} due to {exceptionType}.");
                        logger.Error($"Error details: {e}");
                    }

                    Thread.Sleep(retryDelayMs);
                }
            }

            logger.Debug("Trying the next external ID...");
        }

        return string.Empty;
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