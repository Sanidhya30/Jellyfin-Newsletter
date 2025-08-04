using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Scanner;
using Jellyfin.Plugin.Newsletters.Shared.Models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Newsletters.ItemEventNotifier;

/// <summary>
/// Manages item events and notification processing for the Jellyfin Newsletter plugin.
/// </summary>
public class ItemEventManager
{
    private const int MaxRetries = 10;
    private readonly ILibraryManager libManager;
    private readonly IServerApplicationHost _applicationHost;
    private readonly ConcurrentDictionary<Guid, QueuedItemContainer> itemAddedQueue;
    private readonly Scraper myScraper;
    private Logger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemEventManager"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager used to access items in the library.</param>
    /// <param name="applicationHost">The server application host for dependency injection and service scope.</param>
    public ItemEventManager(
        ILibraryManager libraryManager,
        IServerApplicationHost applicationHost)
    {
        logger = new Logger();
        libManager = libraryManager;
        _applicationHost = applicationHost;
        itemAddedQueue = new ConcurrentDictionary<Guid, QueuedItemContainer>();
        myScraper = new Scraper(libManager);
    }

    /// <summary>
    /// Processes all items in the queue, refreshing metadata and scraping data as needed.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ProcessItemsAsync()
    {
        logger.Debug("Processing Items Async");

        // Attempt to process all items in queue.
        var currentItems = itemAddedQueue.ToArray();
        if (currentItems.Length != 0)
        {
            var scope = _applicationHost.ServiceProvider!.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var itemsToProcess = new List<BaseItem>();
                foreach (var (key, container) in currentItems)
                {
                    var item = libManager.GetItemById(key);
                    if (item is null)
                    {
                        // Remove item from queue.
                        itemAddedQueue.TryRemove(key, out _);
                        break;
                    }

                    logger.Debug($"Item {item.Name}");

                    // Metadata not refreshed yet and under retry limit.
                    if (item.ProviderIds.Keys.Count == 0 && container.RetryCount < MaxRetries)
                    {
                        logger.Debug($"Requeue {item.Name}, no provider ids");
                        container.RetryCount++;
                        itemAddedQueue.AddOrUpdate(key, container, (_, _) => container);
                        continue;
                    }
                    else if (item.ProviderIds.Keys.Count != 0)
                    {
                        // Item has provider ids, add to process list.
                        logger.Debug($"Adding {item.Name} to process list");
                        itemsToProcess.Add(item);
                    }

                    // Remove item from queue.
                    itemAddedQueue.TryRemove(key, out _);
                }

                await myScraper.GetSeriesData(itemsToProcess).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Adds an item to the notification queue for processing.
    /// </summary>
    /// <param name="item">The item to be added to the queue.</param>
    public void AddItem(BaseItem item)
    {
        itemAddedQueue.TryAdd(item.Id, new QueuedItemContainer(item.Id));
        logger.Debug($"Queued {item.Name} for notification");
    }
}
