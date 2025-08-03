using Jellyfin.Data.Events.System;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Newsletters.ItemEventNotifier.ITEMEVENTNOTIFIERENTRYPOINT;
using Jellyfin.Plugin.Newsletters.ItemEventNotifier.ITEMEVENTMANAGER;
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
        // serviceCollection.AddScoped<IWebhookClient<DiscordOption>, DiscordClient>();
        // serviceCollection.AddScoped<IWebhookClient<GenericOption>, GenericClient>();
        // serviceCollection.AddScoped<IWebhookClient<GenericFormOption>, GenericFormClient>();
        // serviceCollection.AddScoped<IWebhookClient<GotifyOption>, GotifyClient>();
        // serviceCollection.AddScoped<IWebhookClient<PushbulletOption>, PushbulletClient>();
        // serviceCollection.AddScoped<IWebhookClient<PushoverOption>, PushoverClient>();
        // serviceCollection.AddScoped<IWebhookClient<SlackOption>, SlackClient>();
        // serviceCollection.AddScoped<IWebhookClient<SmtpOption>, SmtpClient>();
        // serviceCollection.AddScoped<IWebhookClient<MqttOption>, MqttClient>();

        // // Register sender.
        // serviceCollection.AddScoped<IWebhookSender, WebhookSender>();

        // // Register MqttClients
        // serviceCollection.AddSingleton<IMqttClients, MqttClients>();

        // /*-- Register event consumers. --*/
        // // Library consumers.
        // serviceCollection.AddScoped<IEventConsumer<SubtitleDownloadFailureEventArgs>, SubtitleDownloadFailureNotifier>();
        // serviceCollection.AddSingleton<IItemAddedManager, ItemAddedManager>();
        // serviceCollection.AddSingleton<IItemDeletedManager, ItemDeletedManager>();

        // // Security consumers.
        // serviceCollection.AddScoped<IEventConsumer<AuthenticationRequestEventArgs>, AuthenticationFailureNotifier>();
        // serviceCollection.AddScoped<IEventConsumer<AuthenticationResultEventArgs>, AuthenticationSuccessNotifier>();

        // // Session consumers.
        // serviceCollection.AddScoped<IEventConsumer<PlaybackStartEventArgs>, PlaybackStartNotifier>();
        // serviceCollection.AddScoped<IEventConsumer<PlaybackStopEventArgs>, PlaybackStopNotifier>();
        // serviceCollection.AddScoped<IEventConsumer<PlaybackProgressEventArgs>, PlaybackProgressNotifier>();
        // serviceCollection.AddScoped<IEventConsumer<SessionStartedEventArgs>, SessionStartNotifier>();

        // // System consumers.
        // serviceCollection.AddScoped<IEventConsumer<PendingRestartEventArgs>, PendingRestartNotifier>();
        // serviceCollection.AddScoped<IEventConsumer<TaskCompletionEventArgs>, TaskCompletedNotifier>();

        serviceCollection.AddSingleton<ItemEventManager>();

        serviceCollection.AddHostedService<ItemEventNotifierEntryPoint>();
    }
}
