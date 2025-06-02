using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Session;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Insomniac;

/// <summary>
/// Register Insomniac services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        /*-- Register event consumers. --*/
        // Session consumers.
        serviceCollection.AddScoped<IEventConsumer<SessionStartedEventArgs>, PreventSessionIdle>();
        serviceCollection.AddScoped<IEventConsumer<SessionEndedEventArgs>, PreventSessionIdle>();
        // TODO: handle tasks, including scheduling rtc wake???
        // serviceCollection.AddScoped<IEventConsumer<PlaybackStartedEventArgs>, PreventSessionIdle>();
        // cserviceCollection.AddScoped<IEventConsumer<PlaybackStopEventArgs>, PreventSessionIdle>();
    }
}
