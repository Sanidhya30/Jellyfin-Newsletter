using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Newsletters.ItemEventNotifier;

/// <summary>
/// Notifier when a library item is added or removed.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ItemEventNotifierEntryPoint"/> class.
/// </remarks>
/// <param name="itemEventManager">The item event manager.</param>
/// <param name="libraryManager">The library manager.</param>
/// <param name="loggerInstance">The logger instance.</param>
public class ItemEventNotifierEntryPoint(
    ItemEventManager itemEventManager,
    ILibraryManager libraryManager,
    Logger loggerInstance) : IHostedService
{
    private readonly ItemEventManager itemManager = itemEventManager;
    private readonly ILibraryManager libManager = libraryManager;
    private readonly Logger logger = loggerInstance;

    /// <summary>
    /// Gets the current plugin configuration.
    /// </summary>
    private PluginConfiguration Config => Plugin.Instance!.Configuration;

    private void ItemAddedHandler(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        HandleItemChange(itemChangeEventArgs, "add", itemManager.AddItem);
    }

    private void ItemDeletedHandler(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        HandleItemChange(itemChangeEventArgs, "delete", itemManager.DeleteItem);
    }

    private void HandleItemChange(ItemChangeEventArgs e, string eventName, Action<BaseItem> action)
    {
        var item = e.Item;
        if (item.IsVirtualItem)
        {
            return;
        }

        string? itemTypeName = null;

        if (item is Movie)
        {
            itemTypeName = "movie";
        }
        else if (item is Episode)
        {
            itemTypeName = "episode";
        }

        if (itemTypeName is not null)
        {
            logger.Debug($"Item {eventName} event detected for {itemTypeName}: {item.Name}");
            action(item);
        }
        else
        {
            logger.Debug($"Item {eventName} event ignored for {item.GetType().Name}: {item.Name}");
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        libManager.ItemAdded += ItemAddedHandler;
        libManager.ItemRemoved += ItemDeletedHandler;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        libManager.ItemAdded -= ItemAddedHandler;
        libManager.ItemRemoved -= ItemDeletedHandler;
        return Task.CompletedTask;
    }
}
