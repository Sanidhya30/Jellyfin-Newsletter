using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.LOGGER;
using Jellyfin.Plugin.Newsletters.Scripts.Models;
using Jellyfin.Plugin.Newsletters.Scripts.SCRAPER;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Newsletters.ItemEventNotifier.ITEMEVENTMANAGER;

public class ItemEventManager
{
    private const int MaxRetries = 10;
    private Logger logger;
    private readonly ILibraryManager libManager;
    private readonly IServerApplicationHost _applicationHost;
    private readonly ConcurrentDictionary<Guid, QueuedItemContainer> itemAddedQueue;
    private readonly Scraper myScraper;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemEventManager"/> class.
    /// </summary>
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

    /// <inheritdoc />
    public void AddItem(BaseItem item)
    {
        itemAddedQueue.TryAdd(item.Id, new QueuedItemContainer(item.Id));
        logger.Debug($"Queued {item.Name} for notification");
    }
}
