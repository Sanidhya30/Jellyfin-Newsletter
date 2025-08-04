#pragma warning disable 1591, SYSLIB0014, CA1002, CS0162, SA1005 // remove SA1005 for cleanup
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Jellyfin.Plugin.Newsletters.Clients;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using Newtonsoft.Json;
using SQLitePCL.pretty;
// using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Newsletters.Clients.Emails;

public class HtmlBuilder : ClientBuilder
{
    // Global Vars
    // Readonly
    private readonly string newslettersDir;
    private readonly string newsletterHTMLFile;
    // private readonly string[] itemJsonKeys = 

    private string emailBody;

    // Non-readonly
    private static string append = "Append";
    private static string write = "Overwrite";
    // private List<string> fileList;

    public HtmlBuilder()
    {
        DefaultBodyAndEntry(); // set default body and entry HTML from template file if not set in config

        emailBody = Config.Body;

        newslettersDir = Config.NewsletterDir; // newsletterdir
        Directory.CreateDirectory(newslettersDir);

        // if no newsletter filename is saved or the file doesn't exist
        if (Config.NewsletterFileName.Length == 0 || File.Exists(newslettersDir + Config.NewsletterFileName))
        {
            // use date to create filename
            string currDate = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            newsletterHTMLFile = newslettersDir + currDate + "_Newsletter.html";
        }
        else
        {
            newsletterHTMLFile = newslettersDir + Config.NewsletterFileName;
        }

        Logger.Info("Newsletter will be saved to: " + newsletterHTMLFile);
    }

    public string GetDefaultHTMLBody()
    {
        emailBody = Config.Body;
        return emailBody;
    }

    public string TemplateReplace(string htmlObj, string replaceKey, object replaceValue, bool finalPass = false)
    {
        Logger.Debug("Replacing {} params:\n " + htmlObj);
        if (replaceValue is null)
        {
            Logger.Debug($"Replace string is null.. Nothing to replace");
            return htmlObj;
        }

        if (replaceKey == "{RunTime}" && (int)replaceValue == 0)
        {
            Logger.Debug($"{replaceKey} == {replaceValue}");
            Logger.Debug("Skipping replace..");
            return htmlObj;
        }

        Logger.Debug($"Replace Value {replaceKey} with " + replaceValue);

        // Dictionary<string, object> html_params = new Dictionary<string, object>();
        // html_params.Add("{Date}", DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        // html_params.Add(replaceKey, replaceValue);

        htmlObj = htmlObj.Replace(replaceKey, replaceValue.ToString(), StringComparison.Ordinal);
        // Logger.Debug("HERE\n " + htmlObj)

        // foreach (KeyValuePair<string, object> param in html_params)
        // {
        //     if (param.Value is not null)
        //     {
        //         htmlObj = htmlObj.Replace(param.Key, param.Value.ToString(), StringComparison.Ordinal);
        //         // Logger.Debug("HERE\n " + htmlObj)
        //     }
        // }
        
        Logger.Debug("New HTML OBJ: \n" + htmlObj);
        return htmlObj;
    }

    public List<(string HtmlString, List<(MemoryStream? ImageStream, string ContentId)> Images)> BuildChunkedHtmlStringsFromNewsletterData()
    {
        List<string> completed = new List<string>();
        var chunks = new List<(string, List<(MemoryStream?, string)>)>();

        StringBuilder currentChunkBuilder = new StringBuilder();
        var currentChunkImages = new List<(MemoryStream?, string)>();
        int currentChunkBytes = 0;
        const int overheadPerMail = 50000;
        int maxChunkSizeBytes = Config.EmailSize * 1024 * 1024; // Convert MB to bytes
        Logger.Debug($"Max email size set to {maxChunkSizeBytes} bytes");

        try
        {
            Db.CreateConnection();

            foreach (var row in Db.Query("SELECT * FROM CurrNewsletterData;"))
            {
                if (row is not null)
                {
                    JsonFileObj item = JsonHelper.ConvertToObj(row);
                    if (completed.Contains(item.Title))
                    {
                        continue;
                    }

                    string seaEpsHtml = string.Empty;
                    if (item.Type == "Series")
                    {
                        var parsedInfoList = ParseSeriesInfo(item);
                        seaEpsHtml += GetSeasonEpisodeHTML(parsedInfoList);
                    }

                    var tmp_entry = Config.Entry;

                    // Track image size if needed
                    int entryImageBytes = 0;
                    (MemoryStream?, string) imgToAdd = default;
                    if (Config.PosterType == "attachment") 
                    {
                        var (resizedStream, contentId, success) = ResizeImage(item.PosterPath);

                        item.ImageURL = $"cid:{contentId}";
                        entryImageBytes = (resizedStream != null) ? (int)Math.Ceiling(resizedStream.Length * 4.0 / 3.0) : 0; // Base64 encoding overhead;
                        imgToAdd = (resizedStream, contentId);
                    }

                    foreach (var ele in item.GetReplaceDict())
                    {
                        if (ele.Value is not null)
                        {
                            tmp_entry = this.TemplateReplace(tmp_entry, ele.Key, ele.Value);
                        }
                    }

                    // Compose the entry's HTML now (for accurate size)
                    string entryHTML = tmp_entry
                        .Replace("{SeasonEpsInfo}", seaEpsHtml, StringComparison.Ordinal)
                        .Replace("{ServerURL}", Config.Hostname, StringComparison.Ordinal);

                    int entryBytes = Encoding.UTF8.GetByteCount(entryHTML) + entryImageBytes;

                    Logger.Debug($"Processing item: {item.Title}, Size: {entryBytes} bytes, Current Chunk Size: {currentChunkBytes} bytes");
                    if (currentChunkBuilder.Length > 0 && (currentChunkBytes + entryBytes + overheadPerMail) > maxChunkSizeBytes)
                    {
                        // finalize current chunk as one part (HTML fragment)
                        Logger.Debug($"Email size exceeded, finalizing current chunk. Size : {currentChunkBytes} bytes");
                        chunks.Add((currentChunkBuilder.ToString(), new List<(MemoryStream?, string)>(currentChunkImages)));
                        currentChunkBuilder.Clear();
                        currentChunkImages.Clear();
                        currentChunkBytes = 0;
                    }

                    currentChunkBuilder.Append(entryHTML);
                    currentChunkImages.Add(imgToAdd);
                    currentChunkBytes += entryBytes;

                    completed.Add(item.Title);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }
        finally
        {
            Db.CloseConnection();
        }

        // Add final chunk if any
        if (currentChunkBuilder.Length > 0)
        {
            Logger.Debug($"Adding final chunk. Size : {currentChunkBytes} bytes");
            chunks.Add((currentChunkBuilder.ToString(), currentChunkImages));
        }

        return chunks;
    }

    public string BuildHtmlStringsForTest()
    {
        string entryHTML = string.Empty;

        try
        {
            JsonFileObj item = JsonHelper.GetTestObj();
            Logger.Debug("Test Entry ITEM: " + JsonConvert.SerializeObject(item));

            string seaEpsHtml = "Season: 1 - Eps. 1 - 10<br>Season: 2 - Eps. 1 - 10<br>Season: 3 - Eps. 1 - 10";

            string tmp_entry = Config.Entry;

            foreach (KeyValuePair<string, object?> ele in item.GetReplaceDict())
            {
                if (ele.Value is not null)
                {
                    tmp_entry = this.TemplateReplace(tmp_entry, ele.Key, ele.Value);
                }
            }

            // Compose the entry's HTML now
            entryHTML = tmp_entry
                .Replace("{SeasonEpsInfo}", seaEpsHtml, StringComparison.Ordinal)
                .Replace("{ServerURL}", Config.Hostname, StringComparison.Ordinal);
        }
        catch (Exception e)
        {
            Logger.Error("An error has occured: " + e);
        }

        return entryHTML;
    }

    public string ReplaceBodyWithBuiltString(string body, string nlData)
    {
        return body.Replace("{EntryData}", nlData, StringComparison.Ordinal);
    }

    private string GetSeasonEpisodeHTML(List<NlDetailsJson> list)
    {
        string html = string.Empty;
        foreach (NlDetailsJson obj in list)
        {
            Logger.Debug("SNIPPET OBJ: " + JsonConvert.SerializeObject(obj));
            // html += "<div id='SeasonEpisode' class='text' style='color: #FFFFFF;'>Season: " + obj.Season + " - Eps. " + obj.EpisodeRange + "</div>";
            html += "Season: " + obj.Season + " - Eps. " + obj.EpisodeRange + "<br>";
        }

        return html;
    }

    public void CleanUp(string htmlBody)
    {
        // save newsletter to file
        Logger.Info("Saving HTML file");
        WriteFile(write, newsletterHTMLFile, htmlBody);
    }

    private void WriteFile(string method, string path, string value)
    {
        if (method == append)
        {
            File.AppendAllText(path, value);
        }
        else if (method == write)
        {
            File.WriteAllText(path, value);
        }
    }

    private void DefaultBodyAndEntry()
    {
        Logger.Debug("Checking for default Body and Entry HTML from Template file..");
        try
        {
            var pluginDir = Path.GetDirectoryName(typeof(HtmlBuilder).Assembly.Location);
            if (pluginDir == null)
            {
                Logger.Error("Failed to locate plugin directory.");
            }
            
            if (string.IsNullOrWhiteSpace(Config.Body)) 
            {
                try
                {
                    Config.Body = File.ReadAllText($"{pluginDir}/Templates/template_modern_body.html");
                    Logger.Debug("Body HTML set from Template file!");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to set default Body HTML from Template file");
                    Logger.Error(ex);
                }
            }

            if (string.IsNullOrWhiteSpace(Config.Entry))
            {
                try
                {
                    Config.Entry = File.ReadAllText($"{pluginDir}/Templates/template_modern_entry.html");
                    Logger.Debug("Entry HTML set from Template file!");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to set default Entry HTML from Template file");
                    Logger.Error(ex);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error("Failed to locate/set html body from template file..");
            Logger.Error(e);
        }
    }
}