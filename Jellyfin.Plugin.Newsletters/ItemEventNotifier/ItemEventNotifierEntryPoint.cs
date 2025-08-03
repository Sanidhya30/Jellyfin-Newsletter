using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Configuration;
using Jellyfin.Plugin.Newsletters.ItemEventNotifier.ITEMEVENTMANAGER;
using Jellyfin.Plugin.Newsletters.LOGGER;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Newsletters.ItemEventNotifier.ITEMEVENTNOTIFIERENTRYPOINT;

/// <summary>
/// Notifier when a library item is added.
/// </summary>
public class ItemEventNotifierEntryPoint : IHostedService
{
    private readonly PluginConfiguration config;
    private readonly ItemEventManager itemManager;
    private readonly ILibraryManager libManager;
    private Logger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemEventNotifierEntryPoint"/> class.
    /// </summary>
    public ItemEventNotifierEntryPoint(
        ItemEventManager itemEventManager,
        ILibraryManager libraryManager)
    {
        config = Plugin.Instance!.Configuration;
        itemManager = itemEventManager;
        libManager = libraryManager;
        logger = new Logger();
    }

    private void ItemAddedHandler(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        // Never notify on virtual items.
        if (itemChangeEventArgs.Item.IsVirtualItem)
        {
            return;
        }
        
        var itemType = itemChangeEventArgs.Item.GetType();

        if (config.SeriesEnabled && itemType == typeof(Episode))
        {
            // Notify on series items.
            logger.Debug($"Item event detected for episode: {itemChangeEventArgs.Item.Name}");
        }
        else if (config.MoviesEnabled && itemType == typeof(Movie))
        {
            // Notify on movie items.
            logger.Debug($"Item event detected for movie: {itemChangeEventArgs.Item.Name}");
        }
        else
        {
            // Ignore other types of items.
            return;
        }

        itemManager.AddItem(itemChangeEventArgs.Item);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        libManager.ItemAdded += ItemAddedHandler;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        libManager.ItemAdded -= ItemAddedHandler;
        return Task.CompletedTask;
    }
}
