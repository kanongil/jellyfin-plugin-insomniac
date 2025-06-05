using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Insomniac.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Insomniac;

/// <summary>
/// The main plugin.
///
/// Sessions can be abondoned and cannot be used to reliably detect usage / activity.
/// Thus we use the SessionActivity signal to trigger an IdleInhibit with a timeout, extending it on each invocation.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IAsyncDisposable
{
    private const string SessionInhibitReason = "Active remote user session(s)";

    private readonly IdleInhibitorManager _idleInhibitorManager;
    private readonly IdleInhibitorManager.IdleInhibitor _sessionIdleInhibitor;
    private readonly ILogger<Plugin> _logger;

    private TimeSpan _activityDelay = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="sessionManager">Instance. </param>
    /// <param name="loggerFactory">Logger.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ISessionManager sessionManager,
        ILoggerFactory loggerFactory)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        _logger = loggerFactory.CreateLogger<Plugin>();
        _idleInhibitorManager = new IdleInhibitorManager(loggerFactory);
        _sessionIdleInhibitor = _idleInhibitorManager.CreateInhibitor(SessionInhibitReason);

        sessionManager.SessionActivity += OnSessionManagerSessionActivity;
    }

    /// <inheritdoc />
    public override string Name => "Insomniac";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("2b815d95-08e3-4fa8-ac90-8cc9e0b1cc66");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }

    private bool IsRemoteSession(SessionInfo session)
    {
        return true;  // TODO: add proper logic
    }

    /// <summary>
    /// Triggers on session activity with a built-in 10 second throttle (per session).
    /// </summary>
    private void OnSessionManagerSessionActivity(object? sender, SessionEventArgs e)
    {
        _logger.LogInformation("SessionActivity from {0}", e.SessionInfo.RemoteEndPoint);

        if (IsRemoteSession(e.SessionInfo))
        {
            _sessionIdleInhibitor.Inhibit(_activityDelay);
        }
    }

    public async ValueTask DisposeAsync() // TODO: use this
    {
        await _sessionIdleInhibitor.DisposeAsync().ConfigureAwait(false);
    }
}
