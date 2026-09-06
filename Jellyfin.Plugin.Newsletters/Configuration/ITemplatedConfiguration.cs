// Collection properties need setters so the plugin configuration can be XML-serialized,
// the same reason PluginConfiguration suppresses these.
#pragma warning disable CA2227
using System.Collections.ObjectModel;
using Jellyfin.Plugin.Newsletters.Shared.Models;

namespace Jellyfin.Plugin.Newsletters.Configuration;

/// <summary>
/// Interface for newsletter configurations that use HTML body/entry templates.
/// Extends <see cref="INewsletterConfiguration"/> with template-specific properties.
/// </summary>
public interface ITemplatedConfiguration : INewsletterConfiguration
{
    /// <summary>
    /// Gets the custom HTML body template. If empty, the default template is used.
    /// </summary>
    string Body { get; }

    /// <summary>
    /// Gets the custom HTML entry template. If empty, the default template is used.
    /// </summary>
    string Entry { get; }

    /// <summary>
    /// Gets the template category (e.g., "Modern", "Matrix").
    /// </summary>
    string TemplateCategory { get; }

    /// <summary>
    /// Gets the custom HTML header template containing section headers for each event type.
    /// Uses template tags with IDs (header-add, header-update, header-delete, header-upcoming).
    /// If empty, the default template file is used.
    /// </summary>
    string Header { get; }

    /// <summary>
    /// Gets or sets the per-library movie counts captured when the last newsletter was sent.
    /// Empty until the first newsletter has been sent.
    /// </summary>
    Collection<StoredLibraryCount> PrevMovieLibraryCounts { get; set; }

    /// <summary>
    /// Gets or sets the per-library series and episode counts captured when the last newsletter
    /// was sent. Empty until the first newsletter has been sent.
    /// </summary>
    Collection<StoredLibraryCount> PrevSeriesLibraryCounts { get; set; }
}
