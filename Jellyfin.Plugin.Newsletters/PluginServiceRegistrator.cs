using Jellyfin.Plugin.Newsletters.Clients;
using Jellyfin.Plugin.Newsletters.Clients.Discord;
using Jellyfin.Plugin.Newsletters.Clients.Emails;
using Jellyfin.Plugin.Newsletters.ItemEventNotifier;
using Jellyfin.Plugin.Newsletters.Scanner;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Newsletters;

/// <summary>
/// Register newsletter services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddScoped<IClient, Smtp>();
        serviceCollection.AddScoped<IClient, DiscordWebhook>();

        serviceCollection.AddSingleton<ItemEventManager>();
        serviceCollection.AddSingleton<Scraper>();
        serviceCollection.AddSingleton<PosterImageHandler>();

        serviceCollection.AddSingleton<Logger>();
        serviceCollection.AddSingleton<SQLiteDatabase>();

        serviceCollection.AddHostedService<ItemEventNotifierEntryPoint>();
    }
}
