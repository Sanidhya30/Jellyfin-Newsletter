using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Newsletters.Preview;

/// <summary>
/// A set of queued files to exclude or re-include, identified by filename.
/// </summary>
public class PreviewSelection
{
    /// <summary>
    /// Gets or sets the filenames to act on.
    /// </summary>
    public IReadOnlyList<string> Filenames { get; set; } = Array.Empty<string>();
}
