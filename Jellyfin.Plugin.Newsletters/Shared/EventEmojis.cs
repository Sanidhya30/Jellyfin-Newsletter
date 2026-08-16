using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Newsletters.Configuration;

namespace Jellyfin.Plugin.Newsletters.Shared;

/// <summary>
/// Resolves the emoji shown in a newsletter section header for each event type.
/// </summary>
public static class EventEmojis
{
    /// <summary>
    /// The emoji used when an event type has no configured value.
    /// </summary>
    public const string Fallback = "🎬";

    /// <summary>
    /// Gets the default emoji for an event type. These match what the bundled templates
    /// hardcoded before the icons became configurable, so upgrading changes nothing on sight.
    /// </summary>
    /// <param name="eventType">The event type (add, update, delete, upcoming).</param>
    /// <returns>The default emoji for that event type.</returns>
    public static string DefaultFor(string? eventType)
    {
        return (eventType ?? string.Empty).ToLowerInvariant() switch
        {
            "add" => "🎬",
            "update" => "🔄",
            "delete" => "🗑️",
            "upcoming" => "📅",
            _ => Fallback
        };
    }

    /// <summary>
    /// Gets the emoji configured for an event type, falling back to its default when unset.
    /// </summary>
    /// <param name="eventType">The event type (add, update, delete, upcoming).</param>
    /// <param name="config">The plugin configuration holding the overrides.</param>
    /// <returns>The emoji to render.</returns>
    public static string Resolve(string? eventType, PluginConfiguration config)
    {
        string? configured = (eventType ?? string.Empty).ToLowerInvariant() switch
        {
            "add" => config.EventEmojiAdd,
            "update" => config.EventEmojiUpdate,
            "delete" => config.EventEmojiDelete,
            "upcoming" => config.EventEmojiUpcoming,
            _ => null
        };

        return string.IsNullOrWhiteSpace(configured) ? DefaultFor(eventType) : configured.Trim();
    }

    /// <summary>
    /// Gets the event types that carry a configurable emoji, in newsletter order.
    /// </summary>
    /// <returns>The event type keys.</returns>
    public static IReadOnlyList<string> EventTypes()
    {
        return new[] { "add", "update", "delete", "upcoming" };
    }
}
