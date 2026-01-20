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
    private readonly PluginConfiguration config = Plugin.Instance!.Configuration;
    private readonly ItemEventManager itemManager = itemEventManager;
    private readonly ILibraryManager libManager = libraryManager;
    private readonly Logger logger = loggerInstance;

    private string? GetLibraryId(BaseItem item)
    {
        try
        {
            // Get all virtual folders (libraries)
            var virtualFolders = libManager.GetVirtualFolders();
            
            if (string.IsNullOrEmpty(item.Path))
            {
                logger.Debug($"Item {item.Name} has no path");
                return null;
            }
            
            // logger.Debug($"Looking for library containing path: {item.Path}");
            
            foreach (var folder in virtualFolders)
            {
                foreach (var location in folder.Locations)
                {
                    if (item.Path.StartsWith(location, StringComparison.OrdinalIgnoreCase))
                    {
                        // logger.Debug($"Found library: {folder.Name} (ItemId: {folder.ItemId})");
                        return folder.ItemId;
                    }
                }
            }
            
            // logger.Debug($"No library found for item {item.Name}");
        }
        catch (Exception ex)
        {
            logger.Error($"Error getting library ID for {item.Name}: {ex.Message}");
        }
        return null;
    }

    private bool IsLibrarySelected(BaseItem item)
    {
        var libraryId = GetLibraryId(item);
        if (libraryId == null)
        {
            return false;
        }
        if (item is Movie)
        {
            return config.SelectedMoviesLibraries.Contains(libraryId);
        }
        else if (item is Episode)
        {
            return config.SelectedSeriesLibraries.Contains(libraryId);
        }
        return false;
    }

    private void ItemAddedHandler(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        HandleItemChange(itemChangeEventArgs, "add", itemManager.AddItem);
    }

    private void ItemDeletedHandler(object? sender, ItemChangeEventArgs itemChangeEventArgs)
    {
        HandleItemChange(itemChangeEventArgs, "delete", itemManager.DeleteItem);
    }

    private void PrintItemProperties(BaseItem item, string label = "Item")
    {
        logger.Debug($"========== {label} Properties ==========");
        
        if (item == null)
        {
            logger.Debug("Item is NULL");
            return;
        }
        
        var type = item.GetType();
        logger.Debug($"Type: {type.Name}");
        
        // Get all public properties
        var properties = type.GetProperties(System.Reflection.BindingFlags.Public | 
                                        System.Reflection.BindingFlags.Instance);
        
        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(item);
                
                // Handle different types of values
                if (value == null)
                {
                    logger.Debug($"{prop.Name}: NULL");
                }
                else if (value is System.Collections.IEnumerable && !(value is string))
                {
                    // Handle collections
                    var collection = ((System.Collections.IEnumerable)value).Cast<object>().ToList();
                    logger.Debug($"{prop.Name}: [{collection.Count} items]");
                    if (collection.Count > 0 && collection.Count <= 5)
                    {
                        foreach (var item2 in collection)
                        {
                            logger.Debug($"  - {item2}");
                        }
                    }
                }
                else
                {
                    logger.Debug($"{prop.Name}: {value}");
                }
            }
            catch (Exception e)
            {
                logger.Debug($"{prop.Name}: [Error reading - {e.Message}]");
            }
        }
        
        logger.Debug($"========== End {label} Properties ==========");
    }

    private void HandleItemChange(ItemChangeEventArgs e, string eventName, Action<BaseItem> action)
    {
        var item = e.Item;
        if (item.IsVirtualItem)
        {
            return;
        }
        string? itemTypeName = null;
        if (config.MoviesEnabled && item is Movie && IsLibrarySelected(item))
        {
            itemTypeName = "movie";
        }
        else if (config.SeriesEnabled && item is Episode && IsLibrarySelected(item))
        {
            itemTypeName = "episode";
        }
        PrintItemProperties(item, "some item");
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
