using Jellyfin.Data.Events.System;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Newsletters.Clients;
using Jellyfin.Plugin.Newsletters.Clients.Discord;
using Jellyfin.Plugin.Newsletters.Clients.Emails;
using Jellyfin.Plugin.Newsletters.ItemEventNotifier;
using Jellyfin.Plugin.Newsletters.Scanner;
using Jellyfin.Plugin.Newsletters.Shared.Database;
using MediaBrowser.Common.Updates;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Events.Session;
using MediaBrowser.Controller.Events.Updates;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Tasks;
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
