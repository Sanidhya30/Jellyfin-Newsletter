using System;

namespace Jellyfin.Plugin.Newsletters.Shared.Models;

public enum EventType
{
    Add,
    Delete
}

/// <summary>
/// Queued item container.
/// </summary>
public class QueuedItemContainer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueuedItemContainer"/> class.
    /// </summary>
    /// <param name="id">The item id.</param>
    /// <param name="eventType">The event type.</param>
    public QueuedItemContainer(Guid id, EventType eventType)
    {
        ItemId = id;
        RetryCount = 0;
        Timestamp = DateTime.UtcNow;
        EventType = eventType;
    }

    /// <summary>
    /// Gets or sets the current retry count.
    /// </summary>
    public int RetryCount { get; set; }

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
