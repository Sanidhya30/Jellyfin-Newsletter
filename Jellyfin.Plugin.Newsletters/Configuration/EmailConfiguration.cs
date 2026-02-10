using System;

namespace Jellyfin.Plugin.Newsletters.Configuration;

/// <summary>
/// Represents a single Email/SMTP configuration.
/// </summary>
public class EmailConfiguration
{
    /// <summary>
    /// Gets or sets the unique identifier for this configuration.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the user-friendly name for this configuration.
    /// </summary>
    public string Name { get; set; } = "Email Configuration";

    /// <summary>
    /// Gets or sets the SMTP server address.
    /// </summary>
    public string SMTPServer { get; set; } = "smtp.gmail.com";

    /// <summary>
    /// Gets or sets the SMTP port.
    /// </summary>
    public int SMTPPort { get; set; } = 587;

    /// <summary>
    /// Gets or sets the SMTP username.
    /// </summary>
    public string SMTPUser { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SMTP password.
    /// </summary>
    public string SMTPPass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the visible To address (recipient email).
    /// </summary>
    public string VisibleToAddr { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the BCC address.
    /// </summary>
    public string ToAddr { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the From address.
    /// </summary>
    public string FromAddr { get; set; } = "JellyfinNewsletter@donotreply.com";

    /// <summary>
    /// Gets or sets the email subject.
    /// </summary>
    public string Subject { get; set; } = "Jellyfin Newsletter";

    /// <summary>
    /// Gets or sets the email body HTML template.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the entry HTML template.
    /// </summary>
    public string Entry { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the template category (e.g., "Modern").
    /// </summary>
    public string TemplateCategory { get; set; } = "Modern";
}
