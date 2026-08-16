using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Newsletters.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Newsletters.Shared;

/// <summary>
/// Resolves the emoji shown in a newsletter section header for each Jellyfin library.
/// </summary>
public static class LibraryEmojis
{
    /// <summary>
    /// The emoji used when a library's collection type has no specific default.
    /// </summary>
    public const string Fallback = "🎞️";

    private static readonly Dictionary<CollectionTypeOptions, string> DefaultsByCollectionType = new()
    {
        { CollectionTypeOptions.movies, "🍿" },
        { CollectionTypeOptions.tvshows, "📺" },
        { CollectionTypeOptions.music, "🎵" },
        { CollectionTypeOptions.musicvideos, "🎤" },
        { CollectionTypeOptions.books, "📚" },
        { CollectionTypeOptions.homevideos, "📹" },
        { CollectionTypeOptions.boxsets, "📦" },
        { CollectionTypeOptions.mixed, "🎞️" }
    };

    /// <summary>
    /// Gets the default emoji for a collection type, used when the user has not set an override.
    /// </summary>
    /// <param name="collectionType">The library's collection type, if known.</param>
    /// <returns>The default emoji for that collection type.</returns>
    public static string DefaultFor(CollectionTypeOptions? collectionType)
    {
        if (collectionType.HasValue && DefaultsByCollectionType.TryGetValue(collectionType.Value, out var emoji))
        {
            return emoji;
        }

        return Fallback;
    }

    /// <summary>
    /// Builds a dictionary mapping library name to the emoji its section headers should use.
    /// </summary>
    /// <remarks>
    /// Keyed by name because that is all a section header has at render time, while the
    /// configured overrides are keyed by library ID so that renaming a library keeps its emoji.
    /// </remarks>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="logger">The logger used to report lookup failures.</param>
    /// <param name="overrides">The user's per-library emoji overrides.</param>
    /// <returns>A dictionary mapping library names to emoji. Empty if the lookup fails.</returns>
    public static Dictionary<string, string> BuildMap(
        ILibraryManager libraryManager,
        Logger logger,
        IEnumerable<LibraryEmojiConfiguration>? overrides)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        var overridesById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in overrides ?? Enumerable.Empty<LibraryEmojiConfiguration>())
        {
            if (!string.IsNullOrEmpty(entry.LibraryId) && !string.IsNullOrWhiteSpace(entry.Emoji))
            {
                overridesById[entry.LibraryId] = entry.Emoji.Trim();
            }
        }

        try
        {
            foreach (var folder in libraryManager.GetVirtualFolders())
            {
                if (string.IsNullOrEmpty(folder.Name) || map.ContainsKey(folder.Name))
                {
                    continue;
                }

                map[folder.Name] = overridesById.TryGetValue(folder.ItemId ?? string.Empty, out var custom)
                    ? custom
                    : DefaultFor(folder.CollectionType);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Error building library emoji map: {ex.Message}");
        }

        return map;
    }

    /// <summary>
    /// Gets the emoji for a library name, falling back when the library is unknown.
    /// </summary>
    /// <param name="libraryName">The library name shown in the section header.</param>
    /// <param name="map">The map produced by <see cref="BuildMap"/>.</param>
    /// <returns>The emoji to render.</returns>
    public static string Resolve(string? libraryName, Dictionary<string, string> map)
    {
        if (!string.IsNullOrEmpty(libraryName) && map.TryGetValue(libraryName, out var emoji))
        {
            return emoji;
        }

        return Fallback;
    }
}
