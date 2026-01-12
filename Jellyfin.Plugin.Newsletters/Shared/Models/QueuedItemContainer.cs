using System;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Newsletters.Shared.Models;

/// <summary>
/// Represents the type of event that occurred for a queued item.
/// </summary>
public enum EventType
{
    /// <summary>
    /// Item was added to the library.
    /// </summary>
    Add,

    /// <summary>
    /// Item was deleted from the library.
    /// </summary>
    Delete,

    /// <summary>
    /// Item was updated in the library.
    /// </summary>
    Update
}

/// <summary>
/// Queued item container.
/// </summary>
public class QueuedItemContainer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueuedItemContainer"/> class.
    /// </summary>
    /// <param name="item">The media item.</param>
    /// <param name="eventType">The event type.</param>
    public QueuedItemContainer(BaseItem item, EventType eventType)
    {
        Item = item;
        ItemId = item.Id;
        RetryCount = 0;
        Timestamp = DateTime.UtcNow;
        EventType = eventType;
    }

    /// <summary>
    /// Gets or sets the current retry count.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Gets the current item.
    /// </summary>
    public BaseItem Item { get; }

    /// <summary>
    /// Gets the current item id.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the timestamp of when the item was queued.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Gets the event type.
    /// </summary>
    public EventType EventType { get; }
}
