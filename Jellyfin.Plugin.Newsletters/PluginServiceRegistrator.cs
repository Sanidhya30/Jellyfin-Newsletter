using Jellyfin.Plugin.Newsletters.Clients;
using Jellyfin.Plugin.Newsletters.Clients.Discord;
using Jellyfin.Plugin.Newsletters.Clients.Email;
using Jellyfin.Plugin.Newsletters.Clients.Telegram;
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
        // Register the clients
        serviceCollection.AddScoped<IClient, SmtpMailer>();
        serviceCollection.AddScoped<IClient, DiscordWebhook>();
        serviceCollection.AddScoped<IClient, TelegramClient>();

        // Register the item related services
        serviceCollection.AddSingleton<ItemEventManager>();
        serviceCollection.AddSingleton<Scraper>();
        serviceCollection.AddSingleton<PosterImageHandler>();

        // Register the core services
        serviceCollection.AddSingleton<Logger>();
        serviceCollection.AddSingleton<SQLiteDatabase>();

        // Register the entry point for item event notifications
        serviceCollection.AddHostedService<ItemEventNotifierEntryPoint>();
    }
}
