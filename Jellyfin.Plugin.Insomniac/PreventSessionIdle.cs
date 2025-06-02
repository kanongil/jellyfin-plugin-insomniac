using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Session;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Insomniac;

/// <summary>
/// The main logic.
/// </summary>
public sealed class PreventSessionIdle : IEventConsumer<SessionStartedEventArgs>, IEventConsumer<SessionEndedEventArgs>
{
    private readonly ILogger<PreventSessionIdle> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreventSessionIdle"/> class.
    /// </summary>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="authenticationManager">Instance of the <see cref="IHttpContextAccessor"/> interface.</param>
    /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
    /// <param name="deviceManager">Instance of the <see cref="IDeviceManager"/> interface.</param>
    public PreventSessionIdle(
        [NotNull] ISessionManager sessionManager,
        [NotNull] IHttpContextAccessor authenticationManager,
        [NotNull] ILoggerFactory loggerFactory,
        [NotNull] IDeviceManager deviceManager)
    {
        _logger = loggerFactory.CreateLogger<PreventSessionIdle>();
        _logger.LogInformation("Created");
    }

    public async Task OnEvent(SessionStartedEventArgs eventArgs)
    {
        _logger.LogInformation("SessionStarted");
    }

    public async Task OnEvent(SessionEndedEventArgs eventArgs)
    {
        _logger.LogInformation("SessionEnded");
    }
}
