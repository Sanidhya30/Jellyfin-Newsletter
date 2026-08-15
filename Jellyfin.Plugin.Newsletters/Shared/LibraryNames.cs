using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Newsletters.Shared;

/// <summary>
/// Resolves Jellyfin library (virtual folder) IDs to their display names.
/// </summary>
public static class LibraryNames
{
    /// <summary>
    /// Builds a dictionary mapping library ID to library name.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="logger">The logger used to report lookup failures.</param>
    /// <returns>A dictionary mapping library IDs to names. Empty if the lookup fails.</returns>
    public static Dictionary<string, string> BuildMap(ILibraryManager libraryManager, Logger logger)
    {
        var map = new Dictionary<string, string>();

        try
        {
            foreach (var folder in libraryManager.GetVirtualFolders())
            {
                if (!string.IsNullOrEmpty(folder.ItemId) && !map.ContainsKey(folder.ItemId))
                {
                    map[folder.ItemId] = folder.Name;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Error building library name map: {ex.Message}");
        }

        return map;
    }

    /// <summary>
    /// Gets the library name for a library ID, falling back to "Library" when unknown.
    /// </summary>
    /// <param name="libraryId">The library ID to look up.</param>
    /// <param name="map">The map produced by <see cref="BuildMap"/>.</param>
    /// <returns>The library name, or "Library" if the ID is not present.</returns>
    public static string Resolve(string? libraryId, Dictionary<string, string> map)
    {
        if (!string.IsNullOrEmpty(libraryId) && map.TryGetValue(libraryId, out var name))
        {
            return name;
        }

        return "Library";
    }
}
