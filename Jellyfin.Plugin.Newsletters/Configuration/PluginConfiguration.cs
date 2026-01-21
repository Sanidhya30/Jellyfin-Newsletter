#pragma warning disable CA2227, CA1002
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Newsletters.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>    
    public PluginConfiguration()
    {
        Console.WriteLine("[NLP] :: Newsletter Plugin Starting..");

        // set default options here
        DebugMode = false;

        // default Server Details
        SMTPServer = "smtp.gmail.com";
        SMTPPort = 587;
        SMTPUser = string.Empty;
        SMTPPass = string.Empty;

        // default Email Details
        VisibleToAddr = string.Empty;
        ToAddr = string.Empty;         // This is the bcc email address
        FromAddr = "JellyfinNewsletter@donotreply.com";
        Subject = "Jellyfin Newsletter";

        // default Discord Webhook Details
        DiscordWebhookURL = string.Empty;
        DiscordWebhookName = "Jellyfin Newsletter";

        // Attempt Dynamic set of Body and Entry HTML, set empty if failure occurs
        Body = string.Empty;
        Entry = string.Empty;
        
        try
        {
            var pluginDir = Path.GetDirectoryName(typeof(PluginConfiguration).Assembly.Location);
            if (pluginDir == null)
            {
                Console.WriteLine("[NLP] :: [ERR] Failed to locate plugin directory.");
            }
            
            try
            {
                Body = File.ReadAllText($"{pluginDir}/Templates/template_modern_body.html");
                Console.WriteLine("[NLP] :: Body HTML set from Template file!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[NLP] :: Failed to set default Body HTML from Template file");
                Console.WriteLine(ex);
            }

            try
            {
                Entry = File.ReadAllText($"{pluginDir}/Templates/template_modern_entry.html");
                Console.WriteLine("[NLP] :: Entry HTML set from Template file!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[NLP] :: Failed to set default Entry HTML from Template file");
                Console.WriteLine(ex);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("[NLP] :: [ERR] Failed to locate/set html body from template file..");
            Console.WriteLine(e);
        }

        // default Scraper config
        // Deprecating imgur support
        // ApiKey = string.Empty;

        // System Paths
        DataPath = string.Empty;
        TempDirectory = string.Empty;
        PluginsPath = string.Empty;
        ProgramDataPath = string.Empty;
        SystemConfigurationFilePath = string.Empty;
        ProgramSystemPath = string.Empty;
        LogDirectoryPath = string.Empty;

        // default newsletter paths
        NewsletterFileName = string.Empty;
        NewsletterDir = string.Empty;

        // default libraries
        MoviesEnabled = true;
        SeriesEnabled = true;

        // poster type
        // PHType = "Imgur";
        PosterType = "tmdb";
        EmailSize = 15;
        Hostname = string.Empty;

        // default discord fields
        DiscordDescriptionEnabled = true;
        DiscordThumbnailEnabled = true;
        DiscordRatingEnabled = true;
        DiscordPGRatingEnabled = true;
        DiscordDurationEnabled = true;
        DiscordEpisodesEnabled = true;

        // default discord event embed colors for series
        DiscordSeriesAddEmbedColor = "#00ff00";
        DiscordSeriesDeleteEmbedColor = "#ff0000";
        DiscordSeriesUpdateEmbedColor = "#0000ff";
        
        // default discord event embed colors for movies
        DiscordMoviesAddEmbedColor = "#00ff00";
        DiscordMoviesDeleteEmbedColor = "#ff0000";
        DiscordMoviesUpdateEmbedColor = "#0000ff";
        
        // default newsletter event settings
        NewsletterOnItemAddedEnabled = true;
        NewsletterOnItemUpdatedEnabled = false;
        NewsletterOnItemDeletedEnabled = true;

        // default community rating decimal places
        CommunityRatingDecimalPlaces = 1;
    }

    /// <summary>
    /// Gets or sets a value indicating whether debug mode is enabled..
    /// </summary>
    public bool DebugMode { get; set; }

    // Server Details

    /// <summary>
    /// Gets or sets a value indicating whether some true or false setting is enabled..
    /// </summary>
    public string SMTPServer { get; set; }

    /// <summary>
    /// Gets or sets an integer setting.
    /// </summary>
    public int SMTPPort { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string SMTPUser { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string SMTPPass { get; set; }

    // -----------------------------------

    // Email Details

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string ToAddr { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string VisibleToAddr { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string FromAddr { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string Subject { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string Body { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string Entry { get; set; }

    // -----------------------------------

    // Discord Webhook Details

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string DiscordWebhookURL { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string DiscordWebhookName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether description in discord embed should be visible.
    /// </summary>
    public bool DiscordDescriptionEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether thumbnail in discord embed should be visible.
    /// </summary>
    public bool DiscordThumbnailEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether rating in discord embed should be visible.
    /// </summary>
    public bool DiscordRatingEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether PG rating in discord embed should be visible.
    /// </summary>
    public bool DiscordPGRatingEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether duration in discord embed should be visible.
    /// </summary>
    public bool DiscordDurationEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether episodes list in discord embed should be visible.
    /// </summary>
    public bool DiscordEpisodesEnabled { get; set; }

    /// <summary>
    /// Gets or sets the discord series add event embed color.
    /// </summary>
    public string DiscordSeriesAddEmbedColor { get; set; }

    /// <summary>
    /// Gets or sets the discord series delete event embed color.
    /// </summary>
    public string DiscordSeriesDeleteEmbedColor { get; set; }

    /// <summary>
    /// Gets or sets the discord series update event embed color.
    /// </summary>
    public string DiscordSeriesUpdateEmbedColor { get; set; }

    /// <summary>
    /// Gets or sets the discord movies add event embed color.
    /// </summary>
    public string DiscordMoviesAddEmbedColor { get; set; }

    /// <summary>
    /// Gets or sets the discord movies delete event embed color.
    /// </summary>
    public string DiscordMoviesDeleteEmbedColor { get; set; }

    /// <summary>
    /// Gets or sets the discord movies update event embed color.
    /// </summary>
    public string DiscordMoviesUpdateEmbedColor { get; set; }

    // -----------------------------------

    // Scraper Config

    /// <summary>
    /// Gets or sets a value indicating poster type.
    /// </summary>
    public string PosterType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating maximum email size.
    /// </summary>
    public int EmailSize { get; set; }

    /// <summary>
    /// Gets or sets a value for JF hostname accessible outside of network.
    /// </summary>
    public string Hostname { get; set; }

    // / <summary>
    // / Gets or sets a string setting.
    // / </summary>
    // Deprecating imgur support
    // public string ApiKey { get; set; }

    // -----------------------------------

    // System Paths

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string PluginsPath { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string TempDirectory { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string DataPath { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string ProgramDataPath { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string SystemConfigurationFilePath { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string ProgramSystemPath { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string LogDirectoryPath { get; set; }

    // -----------------------------------

    // Newsletter Paths

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string NewsletterFileName { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string NewsletterDir { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Series should be scanned.
    /// </summary>
    public bool SeriesEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Movies should be scanned.
    /// </summary>
    public bool MoviesEnabled { get; set; }

    /// <summary>
    /// Gets or sets the list of selected series libraries.
    /// </summary>
    public Collection<string> SelectedSeriesLibraries { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of selected movies libraries.
    /// </summary>
    public Collection<string> SelectedMoviesLibraries { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether newsletter should be sent when items are added.
    /// </summary>
    public bool NewsletterOnItemAddedEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether newsletter should be sent when items are updated.
    /// </summary>
    public bool NewsletterOnItemUpdatedEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether newsletter should be sent when items are deleted.
    /// </summary>
    public bool NewsletterOnItemDeletedEnabled { get; set; }

    /// <summary>
    /// Gets or sets the number of decimal places to display for community ratings.
    /// </summary>
    public int CommunityRatingDecimalPlaces { get; set; }
}
