using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Scanner;
using Jellyfin.Plugin.Newsletters.Shared.Models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Newsletters.ItemEventNotifier;

/// <summary>
/// Manages item events and notification processing for the Jellyfin Newsletter plugin.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ItemEventManager"/> class.
/// </remarks>
/// <param name="libraryManager">The library manager used to access items in the library.</param>
/// <param name="appHost">The server application host for dependency injection and service scope.</param>
/// <param name="loggerInstance">The logger instance used for logging debug information.</param>
/// <param name="scraperInstance">The scraper instance used for scraping series data.</param>
public class ItemEventManager(
    ILibraryManager libraryManager,
    IServerApplicationHost appHost,
    Logger loggerInstance,
    Scraper scraperInstance)
{
    private const int MaxRetries = 10;
    private readonly ILibraryManager libManager = libraryManager;
    private readonly IServerApplicationHost applicationHost = appHost;
    private readonly ConcurrentDictionary<Guid, QueuedItemContainer> itemAddedQueue = new();
    private readonly ConcurrentDictionary<Guid, QueuedItemContainer> itemDeletedQueue = new();
    private readonly Scraper myScraper = scraperInstance;
    private readonly Logger logger = loggerInstance;

    /// <summary>
    /// Processes all items in the queue, refreshing metadata and scraping data as needed.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ProcessItemsAsync()
    {
        logger.Debug("Processing Items Async");

        var addedItems = itemAddedQueue.ToArray();
        var deletedItems = itemDeletedQueue.ToArray();

        if (addedItems.Length == 0 && deletedItems.Length == 0)
        {
            return;
        }

        var combinedList = new List<QueuedItemContainer>();

        foreach (var (_, container) in addedItems)
        {
            combinedList.Add(container);
            itemAddedQueue.TryRemove(container.ItemId, out _);
        }

        foreach (var (_, container) in deletedItems)
        {
            combinedList.Add(container);
            itemDeletedQueue.TryRemove(container.ItemId, out _);
        }

        var sortedList = combinedList.OrderBy(i => i.Timestamp).ToList();

        if (sortedList.Count > 0)
        {
            var scope = applicationHost.ServiceProvider!.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var itemsToProcess = new List<BaseItem>();
                foreach (var queueItem in sortedList)
                {
                    if (queueItem.EventType == EventType.Add)
                    {
                        var item = libManager.GetItemById(queueItem.ItemId);
                        if (item is null)
                        {
                            continue;
                        }

                        // Metadata not refreshed yet and under retry limit.
                        if (item.ProviderIds.Keys.Count == 0 && queueItem.RetryCount < MaxRetries)
                        {
                            logger.Debug($"Requeue {item.Name}, no provider ids");
                            queueItem.RetryCount++;
                            itemAddedQueue.AddOrUpdate(queueItem.ItemId, queueItem, (_, _) => queueItem);
                            continue;
                        }
                        else if (item.ProviderIds.Keys.Count != 0)
                        {
                            // Item has provider ids, add to process list.
                            logger.Debug($"Adding {item.Name} to process list");
                            itemsToProcess.Add(item);
                        }
                    }
                }

                await myScraper.GetSeriesData(itemsToProcess, sortedList).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Adds an item to the notification queue for processing.
    /// </summary>
    /// <param name="item">The item to be added to the queue.</param>
    public void AddItem(BaseItem item)
    {
        itemAddedQueue.TryAdd(item.Id, new QueuedItemContainer(item.Id, EventType.Add));
        logger.Debug($"Queued {item.Name} for add notification");
    }

    /// <summary>
    /// Delete an item from the notification queue.
    /// </summary>
    /// <param name="item">The item to be deleted from the queue.</param>
    public void DeleteItem(BaseItem item)
    {
        itemDeletedQueue.TryAdd(item.Id, new QueuedItemContainer(item.Id, EventType.Delete));
        logger.Debug($"Queued {item.Name} for deletion notification");
    }
}
