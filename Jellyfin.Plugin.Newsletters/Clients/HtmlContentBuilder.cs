using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.Integrations;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using Jellyfin.Plugin.Newsletters.Shared.Entities;
using MediaBrowser.Controller.Library;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Newsletters.Clients;

/// <summary>
/// Abstract base class for newsletter builders that produce HTML content using templates.
/// Provides shared logic for template loading, replacement, data querying, sorting, and grouping.
/// </summary>
/// <param name="loggerInstance">The logger instance for logging operations.</param>
/// <param name="dbInstance">The database instance for data access.</param>
/// <param name="libraryManager">The library manager for resolving library names.</param>
/// <param name="upcomingItems">The list of prefetched upcoming media items.</param>
public abstract class HtmlContentBuilder(
    Logger loggerInstance,
    SQLiteDatabase dbInstance,
    ILibraryManager libraryManager,
    IReadOnlyList<JsonFileObj> upcomingItems)
    : ClientBuilder(loggerInstance, dbInstance, libraryManager)
{
    private string bodyHtml = string.Empty;
    private string entryHtml = string.Empty;
    private string headerAddHtml = string.Empty;
    private string headerUpdateHtml = string.Empty;
    private string headerDeleteHtml = string.Empty;
    private string headerUpcomingHtml = string.Empty;
    private bool _isSetup;

    /// <summary>
    /// Gets the list of prefetched upcoming media items.
    /// </summary>
    protected IReadOnlyList<JsonFileObj> UpcomingItems { get; } = upcomingItems;

    /// <summary>
    /// Gets the default template category to use when the configuration doesn't specify one.
    /// </summary>
    protected abstract string DefaultTemplateCategory { get; }

    /// <summary>
    /// Gets the HTML section header for an event type and library name.
    /// Loads the header from the parsed template and substitutes {LibraryName}.
    /// </summary>
    /// <param name="eventType">The event type (add, update, delete, upcoming).</param>
    /// <param name="libraryName">The library name to display.</param>
    /// <returns>An HTML string for the section header.</returns>
    protected virtual string GetEventSectionHeader(string eventType, string libraryName = "Library")
    {
        string headerTemplate = eventType.ToLowerInvariant() switch
        {
            "add" => headerAddHtml,
            "update" => headerUpdateHtml,
            "delete" => headerDeleteHtml,
            "upcoming" => headerUpcomingHtml,
            _ => headerAddHtml
        };

        if (string.IsNullOrWhiteSpace(headerTemplate))
        {
            Logger.Warn($"No header template found for event type '{eventType}'. Using empty string.");
            return string.Empty;
        }

        return this.TemplateReplace(headerTemplate, "{LibraryName}", libraryName);
    }

    /// <summary>
    /// Gets the HTML badge for an event type to be displayed on individual entries.
    /// Each client provides its own markup style.
    /// </summary>
    /// <param name="eventType">The event type (add, update, delete, upcoming).</param>
    /// <returns>An HTML string for the event badge.</returns>
    protected abstract string GetEventBadge(string eventType);

    /// <summary>
    /// Ensures templates are loaded from the configuration or default template files.
    /// </summary>
    /// <param name="config">The templated configuration to use.</param>
    protected void EnsureSetup(ITemplatedConfiguration config)
    {
        if (_isSetup)
        {
            return;
        }

        DefaultBodyAndEntry(config);
        _isSetup = true;
    }

    /// <summary>
    /// Gets the default HTML body for the newsletter from the configuration.
    /// </summary>
    /// <param name="config">The templated configuration.</param>
    /// <returns>The default HTML body string.</returns>
    public string GetDefaultHTMLBody(ITemplatedConfiguration config)
    {
        EnsureSetup(config);
        return bodyHtml;
    }

    /// <summary>
    /// Gets the loaded entry template HTML.
    /// </summary>
    /// <returns>The HTML entry template string.</returns>
    protected string GetEntryHtml()
    {
        return entryHtml;
    }

    /// <summary>
    /// Replaces a specified key in the HTML template with the provided value.
    /// </summary>
    /// <param name="htmlObj">The HTML template string.</param>
    /// <param name="replaceKey">The key to be replaced in the template.</param>
    /// <param name="replaceValue">The value to replace the key with.</param>
    /// <returns>The HTML string with the key replaced by the value.</returns>
    public string TemplateReplace(string htmlObj, string replaceKey, object replaceValue)
    {
        Logger.Debug($"Replacing {replaceKey} param");
        if (replaceValue is null)
        {
            Logger.Debug($"Replace string is null.. Defaulting to N/A");
            replaceValue = "N/A";
        }

        if (replaceKey == "{RunTime}" && (int)replaceValue == 0)
        {
            Logger.Debug($"{replaceKey} == {replaceValue}");
            Logger.Debug("Defaulting to N/A");
            replaceValue = "N/A";
        }

        if (replaceKey == "{CommunityRating}" && replaceValue is float rating)
        {
            replaceValue = rating > 0 ? rating.ToString($"F{Config.CommunityRatingDecimalPlaces}", CultureInfo.InvariantCulture) : "N/A";
        }

        Logger.Debug($"Replace Value {replaceKey} with " + replaceValue);

        htmlObj = htmlObj.Replace(replaceKey, replaceValue.ToString(), StringComparison.Ordinal);

        // Logger.Debug("New HTML OBJ: \n" + htmlObj);
        return htmlObj;
    }

    /// <summary>
    /// Replaces the {EntryData} placeholder in the newsletter body with the provided newsletter data string.
    /// </summary>
    /// <param name="body">The HTML body template containing the {EntryData} placeholder.</param>
    /// <param name="nlData">The newsletter data to insert into the body.</param>
    /// <returns>The HTML body with the {EntryData} placeholder replaced by the newsletter data.</returns>
    public static string ReplaceBodyWithBuiltString(string body, string nlData)
    {
        return body.Replace("{EntryData}", nlData, StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts season/episode data into HTML with line breaks.
    /// </summary>
    /// <param name="list">The parsed series details.</param>
    /// <returns>A string with season/episode info separated by br tags.</returns>
    protected string GetSeasonEpisodeHTML(IReadOnlyCollection<NlDetailsJson> list)
    {
        string baseText = GetSeasonEpisodeBase(list);
        return baseText.TrimEnd('\r', '\n').Replace("\n", "<br>", StringComparison.Ordinal);
    }

    /// <summary>
    /// Queries the database and merges upcoming items, deduplicates, sorts, and groups them
    /// by event type and library name.
    /// </summary>
    /// <param name="config">The newsletter configuration for filtering.</param>
    /// <param name="clientName">The client name for log messages.</param>
    /// <returns>The grouped items structure.</returns>
    protected IEnumerable<EventGroupResult> BuildGroupedItems(INewsletterConfiguration config, string clientName)
    {
        var libraryNameMap = BuildLibraryNameMap();
        var sortedItems = BuildSortedItems(config, UpcomingItems, clientName);

        return sortedItems
            .GroupBy(i => i.EventType?.ToLowerInvariant() ?? "add")
            .Select(eventGroup => new EventGroupResult
            {
                EventType = eventGroup.Key,
                Libraries = eventGroup
                    .GroupBy(i => i.EventType == "upcoming" ? i.LibraryId : GetLibraryName(i.LibraryId, libraryNameMap))
                    .Select(libGroup => new LibraryGroupResult { LibraryName = libGroup.Key, Items = libGroup.ToList() })
            });
    }

    /// <summary>
    /// Applies template replacements for a single item and composes the final entry HTML.
    /// Subclasses can override <see cref="CustomizeItemReplaceDict"/> to modify the replace dictionary
    /// before template replacement (e.g., for Matrix image upload).
    /// </summary>
    /// <param name="item">The item to process.</param>
    /// <param name="eventType">The event type.</param>
    /// <param name="serverId">The server ID for building item URLs.</param>
    /// <returns>The composed entry HTML string.</returns>
    protected string BuildEntryHtml(JsonFileObj item, string eventType, string serverId)
    {
        string seaEpsHtml = string.Empty;
        if (item.Type == "Series")
        {
            var parsedInfoList = ParseSeriesInfo(item, UpcomingItems);
            seaEpsHtml = GetSeasonEpisodeHTML(parsedInfoList);
        }

        var tmpEntry = entryHtml;

        var replaceDict = item.GetReplaceDict();
        CustomizeItemReplaceDict(item, eventType, replaceDict);

        // Add computed replacements to the dict
        replaceDict["{EventBadge}"] = GetEventBadge(eventType);
        replaceDict["{SeasonEpsInfo}"] = seaEpsHtml;
        replaceDict["{ItemURL}"] = string.IsNullOrEmpty(Config.Hostname) || eventType == "upcoming" || eventType == "delete"
            ? string.Empty
            : $"{Config.Hostname}/web/index.html#/details?id={item.ItemID}&serverId={serverId}&event={eventType}";

        foreach (var ele in replaceDict)
        {
            if (ele.Value is not null)
            {
                tmpEntry = this.TemplateReplace(tmpEntry, ele.Key, ele.Value);
            }
        }

        return tmpEntry;
    }

    /// <summary>
    /// Hook for subclasses to customize the replace dictionary before template replacement.
    /// Default implementation does nothing.
    /// </summary>
    /// <param name="item">The item being processed.</param>
    /// <param name="eventType">The event type.</param>
    /// <param name="replaceDict">The replace dictionary to modify.</param>
    protected virtual void CustomizeItemReplaceDict(JsonFileObj item, string eventType, Dictionary<string, object?> replaceDict)
    {
        // Default: no customization. Subclasses can override.
    }

    /// <summary>
    /// Builds a sample HTML string for testing newsletter entry rendering.
    /// </summary>
    /// <param name="config">The templated configuration.</param>
    /// <returns>A string containing HTML for test newsletter entries.</returns>
    protected string BuildTestEntriesHtml(ITemplatedConfiguration config)
    {
        EnsureSetup(config);

        StringBuilder testHTML = new StringBuilder();

        string[] eventTypes = { "add", "update", "delete", "upcoming" };
        string[] titles = { "Test Added Series", "Test Updated Movie", "Test Deleted Series", "Test Upcoming Movie" };

        for (int i = 0; i < eventTypes.Length; i++)
        {
            string eventType = eventTypes[i];

            testHTML.Append(GetEventSectionHeader(eventType));

            JsonFileObj item = JsonFileObj.GetTestObj();
            item.Title = titles[i];

            Logger.Debug($"Test Entry ITEM ({eventType}): " + JsonConvert.SerializeObject(item));

            string seaEpsHtml = "Season: 1 - Eps. 1 - 10<br>Season: 2 - Eps. 1 - 10<br>Season: 3 - Eps. 1 - 10";

            string tmpEntry = entryHtml;

            var replaceDict = item.GetReplaceDict();
            CustomizeTestItemReplaceDict(item, eventType, replaceDict, config);

            // Add computed replacements to the dict
            replaceDict["{EventBadge}"] = GetEventBadge(eventType);
            replaceDict["{SeasonEpsInfo}"] = seaEpsHtml;
            replaceDict["{ItemURL}"] = string.IsNullOrEmpty(Config.Hostname)
                ? string.Empty
                : Config.Hostname;

            foreach (var ele in replaceDict)
            {
                if (ele.Value is not null)
                {
                    tmpEntry = this.TemplateReplace(tmpEntry, ele.Key, ele.Value);
                }
            }

            testHTML.Append(tmpEntry);
        }

        return testHTML.ToString();
    }

    /// <summary>
    /// Hook for subclasses to customize test item replace dictionaries.
    /// Default implementation does nothing.
    /// </summary>
    /// <param name="item">The test item.</param>
    /// <param name="eventType">The event type.</param>
    /// <param name="replaceDict">The replace dictionary to modify.</param>
    /// <param name="config">The templated configuration.</param>
    protected virtual void CustomizeTestItemReplaceDict(JsonFileObj item, string eventType, Dictionary<string, object?> replaceDict, ITemplatedConfiguration config)
    {
        // Default: no customization. Subclasses can override.
    }

    private void DefaultBodyAndEntry(ITemplatedConfiguration config)
    {
        Logger.Debug("Checking for default Body, Entry, and Header HTML from Template file..");

        this.bodyHtml = config.Body ?? string.Empty;
        this.entryHtml = config.Entry ?? string.Empty;
        string headerHtml = config.Header ?? string.Empty;

        try
        {
            var pluginDir = Path.GetDirectoryName(typeof(HtmlContentBuilder).Assembly.Location);
            if (pluginDir == null)
            {
                Logger.Error("Failed to locate plugin directory.");
                return;
            }

            string category = !string.IsNullOrEmpty(config.TemplateCategory) ? config.TemplateCategory : DefaultTemplateCategory;

            if (string.IsNullOrWhiteSpace(this.bodyHtml))
            {
                try
                {
                    string bodyTemplate = File.ReadAllText(Path.Combine(pluginDir, "Templates", category, "template_body.html"));
                    this.bodyHtml = bodyTemplate;
                    Logger.Debug($"Body HTML set from Template file ({category}) internally!");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to set default Body HTML from Template file");
                    Logger.Error(ex);
                }
            }

            if (string.IsNullOrWhiteSpace(this.entryHtml))
            {
                try
                {
                    string entryTemplate = File.ReadAllText(Path.Combine(pluginDir, "Templates", category, "template_entry.html"));
                    this.entryHtml = entryTemplate;
                    Logger.Debug($"Entry HTML set from Template file ({category}) internally!");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to set default Entry HTML from Template file");
                    Logger.Error(ex);
                }
            }

            if (string.IsNullOrWhiteSpace(headerHtml))
            {
                try
                {
                    headerHtml = File.ReadAllText(Path.Combine(pluginDir, "Templates", category, "template_header.html"));
                    Logger.Debug($"Header HTML set from Template file ({category}) internally!");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to set default Header HTML from Template file");
                    Logger.Error(ex);
                }
            }

            // Parse the four header sections from the combined template
            ParseHeaderSections(headerHtml);
        }
        catch (Exception e)
        {
            Logger.Error("Failed to locate/set html body from template file..");
            Logger.Error(e);
        }
    }

    /// <summary>
    /// Parses the combined header HTML into four separate sections using template tag IDs.
    /// Extracts content between &lt;template id="header-{type}"&gt; and &lt;/template&gt; markers.
    /// </summary>
    /// <param name="fullHeaderHtml">The combined header HTML containing all four template sections.</param>
    private void ParseHeaderSections(string fullHeaderHtml)
    {
        if (string.IsNullOrWhiteSpace(fullHeaderHtml))
        {
            Logger.Warn("Header HTML is empty. Section headers will not be rendered.");
            return;
        }

        this.headerAddHtml = ExtractTemplateSection(fullHeaderHtml, "header-add");
        this.headerUpdateHtml = ExtractTemplateSection(fullHeaderHtml, "header-update");
        this.headerDeleteHtml = ExtractTemplateSection(fullHeaderHtml, "header-delete");
        this.headerUpcomingHtml = ExtractTemplateSection(fullHeaderHtml, "header-upcoming");

        Logger.Debug($"Header sections parsed — Add: {!string.IsNullOrEmpty(this.headerAddHtml)}, Update: {!string.IsNullOrEmpty(this.headerUpdateHtml)}, Delete: {!string.IsNullOrEmpty(this.headerDeleteHtml)}, Upcoming: {!string.IsNullOrEmpty(this.headerUpcomingHtml)}");
    }

    /// <summary>
    /// Extracts the inner content of a template tag by its ID.
    /// For example, for id="header-add", extracts content between
    /// <template id="header-add"> and </template>.
    /// </summary>
    /// <param name="html">The full HTML string containing template tags.</param>
    /// <param name="templateId">The ID of the template tag to extract.</param>
    /// <returns>The inner content of the matched template tag, or empty string if not found.</returns>
    private string ExtractTemplateSection(string html, string templateId)
    {
        // Match <template id="header-add"> ... </template> (case-insensitive, single-line mode)
        string pattern = $@"<template\s+id\s*=\s*[""']{Regex.Escape(templateId)}[""']\s*>(.*?)</template>";
        var match = Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        Logger.Warn($"Template section '{templateId}' not found in header HTML.");
        return string.Empty;
    }

    /// <summary>
    /// Represents a group of items for a single event type.
    /// </summary>
    protected class EventGroupResult
    {
        /// <summary>
        /// Gets or sets the event type (add, update, delete, upcoming).
        /// </summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the library groups within this event type.
        /// </summary>
        public IEnumerable<LibraryGroupResult> Libraries { get; set; } = Enumerable.Empty<LibraryGroupResult>();
    }

    /// <summary>
    /// Represents a group of items within a single library.
    /// </summary>
    protected class LibraryGroupResult
    {
        /// <summary>
        /// Gets or sets the library name.
        /// </summary>
        public string LibraryName { get; set; } = string.Empty;

        /// <summary>
        /// Gets the items in this library group.
        /// </summary>
        public IReadOnlyList<JsonFileObj> Items { get; init; } = new List<JsonFileObj>();
    }
}
