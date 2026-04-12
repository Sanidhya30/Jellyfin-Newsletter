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
}
