using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using Jellyfin.Plugin.Insomniac.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
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

    private const string TaskInhibitReason = "Running scheduled task(s)";

    private readonly IdleInhibitorManager _idleInhibitorManager;
    private readonly IdleInhibitorManager.IdleInhibitor _sessionIdleInhibitor;
    private readonly IdleInhibitorManager.IdleInhibitor _taskIdleInhibitor;
    private readonly ILogger<Plugin> _logger;
    private readonly IReadOnlyList<IPData> _localInterfaces;
    private readonly ISessionManager _sessionManager;
    private readonly ITaskManager _taskManager;

    private int _activeTasks;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="networkManager">Instance of the <see cref="INetworkManager"/> interface.</param>
    /// <param name="taskManager">Instance of the <see cref="ITaskManager"/> interface.</param>
    /// <param name="loggerFactory">Logger.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ISessionManager sessionManager,
        INetworkManager networkManager,
        ITaskManager taskManager,
        ILoggerFactory loggerFactory)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        _logger = loggerFactory.CreateLogger<Plugin>();
        _idleInhibitorManager = new IdleInhibitorManager(loggerFactory);
        _sessionIdleInhibitor = _idleInhibitorManager.CreateInhibitor(SessionInhibitReason);
        _taskIdleInhibitor = _idleInhibitorManager.CreateInhibitor(TaskInhibitReason);
        _localInterfaces = networkManager.GetAllBindInterfaces(true);
        _sessionManager = sessionManager;
        _taskManager = taskManager;

        sessionManager.SessionActivity += OnSessionManagerSessionActivity;

        taskManager.TaskExecuting += OnTaskExecuting;
        taskManager.TaskCompleted += OnTaskCompleted;
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

    /// <summary>
    /// A best effort check whether the passed address is same as host.
    /// It always works for loopback addresses.
    /// </summary>
    /// <param name="address">The address to check.</param>
    /// <returns>bool indicating the result.</returns>
    private bool IsHostAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address) || _localInterfaces.Any((@interface) => @interface.Address.Equals(address));
    }

    private bool IsRemoteSession(SessionInfo session)
    {
        return NetworkUtils.TryParseHost(session.RemoteEndPoint, out var addresses, true, true) && !addresses.Any(IsHostAddress);
    }

    /// <summary>
    /// Triggers on session activity with a built-in 10 second throttle (per session).
    /// </summary>
    private void OnSessionManagerSessionActivity(object? sender, SessionEventArgs e)
    {
        _logger.LogDebug("SessionActivity from {0}, remote={1}", e.SessionInfo.RemoteEndPoint, IsRemoteSession(e.SessionInfo));

        if (!Configuration.OnlyInhibitRemote || IsRemoteSession(e.SessionInfo))
        {
            var delay = TimeSpan.FromSeconds(Configuration.ActivityIdleDelaySeconds);
            _sessionIdleInhibitor.Inhibit(delay);
        }
    }

    private void OnTaskExecuting(object? sender, GenericEventArgs<IScheduledTaskWorker> e)
    {
        _logger.LogDebug("TaskExecuting: {0}", e.Argument.Name);

        _activeTasks++;
        _taskIdleInhibitor.Inhibit();
    }

    private void OnTaskCompleted(object? sender, TaskCompletionEventArgs e)
    {
        _activeTasks--;
        if (_activeTasks == 0)
        {
            _taskIdleInhibitor.UnInhibit();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() // TODO: use this
    {
        _sessionManager.SessionActivity -= OnSessionManagerSessionActivity;
        _taskManager.TaskExecuting -= OnTaskExecuting;
        _taskManager.TaskCompleted -= OnTaskCompleted;

        await _sessionIdleInhibitor.DisposeAsync().ConfigureAwait(false);
        await _taskIdleInhibitor.DisposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }
}
