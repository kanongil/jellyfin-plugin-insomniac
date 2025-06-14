using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Jellyfin.Data.Events;
using Jellyfin.Plugin.Insomniac.Configuration;
using Jellyfin.Plugin.Insomniac.Inhibitors;
using Jellyfin.Plugin.Insomniac.Mdns;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Insomniac;

/// <summary>
/// The main plugin.
///
/// Sessions can be abondoned and cannot be used to reliably detect usage / activity.
/// Thus we use the SessionActivity signal to trigger an IdleInhibit with a timeout, extending it on each invocation.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IDisposable
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
    private readonly IServerApplicationHost _appHost;
    private readonly IConfigurationManager _configurationManager;
    private readonly CancellationTokenRegistration _startedCallback;
    private readonly CancellationTokenRegistration _shutdownCallback;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private IServicePublisher? _servicePublisher;
    private int _activeTasks;
    private MdnsConfig? _mdnsConfig;

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
        IServerApplicationHost appHost,
        IHostApplicationLifetime hostApplicationLifetime,
        IConfigurationManager configurationManager,
        ILoggerFactory loggerFactory)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        _logger = loggerFactory.CreateLogger<Plugin>();
        _idleInhibitorManager = new IdleInhibitorManager(loggerFactory);
        _sessionIdleInhibitor = _idleInhibitorManager.CreateInhibitor(InhibitorType.NetworkClient, SessionInhibitReason);
        _taskIdleInhibitor = _idleInhibitorManager.CreateInhibitor(InhibitorType.SystemIdle, TaskInhibitReason);
        _localInterfaces = networkManager.GetAllBindInterfaces(true);
        _sessionManager = sessionManager;
        _taskManager = taskManager;
        _appHost = appHost;
        _configurationManager = configurationManager;

        sessionManager.SessionActivity += OnSessionManagerSessionActivity;
        sessionManager.PlaybackProgress += OnPlaybackProgress;

        taskManager.TaskExecuting += OnTaskExecuting;
        taskManager.TaskCompleted += OnTaskCompleted;

        _startedCallback = hostApplicationLifetime.ApplicationStarted.Register(OnApplicationStarted);
        _shutdownCallback = hostApplicationLifetime.ApplicationStopping.Register(OnApplicationStopping);

        configurationManager.ConfigurationUpdated += OnConfigurationUpdated;
        ConfigurationChanged += OnConfigurationChanged;

        //configurationManager.GetNetworkConfiguration().RequireHttps
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

    private void HandleUserActivity(SessionInfo sessionInfo)
    {
        if (!Configuration.OnlyInhibitRemote || IsRemoteSession(sessionInfo))
        {
            var delay = TimeSpan.FromSeconds(Configuration.ActivityIdleDelaySeconds);
            _sessionIdleInhibitor.Inhibit(delay);
        }
    }

    /// <summary>
    /// Triggers on session activity with a built-in 10 second throttle (per session).
    /// </summary>
    private void OnSessionManagerSessionActivity(object? sender, SessionEventArgs e)
    {
        _logger.LogDebug("SessionActivity from {0}, remote={1}", e.SessionInfo.RemoteEndPoint, IsRemoteSession(e.SessionInfo));

        HandleUserActivity(e.SessionInfo);
    }

    /// <summary>
    /// Called periodically while a session plays contents, including when paused.
    /// </summary>
    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        _logger.LogDebug("OnPlaybackProgress from {0}, remote={1}", e.Session.RemoteEndPoint, IsRemoteSession(e.Session));

        HandleUserActivity(e.Session);
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

    private async void SyncMdns()
    {
        MdnsConfig config = new(Configuration.EnableMdns, _appHost.FriendlyName, _appHost.ListenWithHttps, _appHost.HttpPort, _appHost.HttpsPort);

        await _syncLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (config.Equals(_mdnsConfig))
            {
                return;
            }

            _mdnsConfig = config;

            if (!config.Enabled)
            {
                _servicePublisher?.Dispose();
                _servicePublisher = null;
                return;
            }

            // Apply new configuration

            try
            {
                _servicePublisher ??= RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? new CFNetPublisher() : new AvahiPublisher();

                // TODO: verify networkManager.GetBindAddress() is on local interface or 0.0.0.0

                var serviceType = config.ListenWithHttps ? "_https._tcp" : "_http._tcp";
                var port = config.ListenWithHttps ? config.HttpsPort : config.HttpPort;
                ServiceConfig serviceConfig = new(serviceType, subType: "_jellyfin", config.FriendlyName, port);

                _logger.LogInformation("Publishing Bonjour/Zeroconf service using {0}", _servicePublisher);
                await _servicePublisher.PublishConfig(serviceConfig).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogWarning("Failed to register Bonjour/Zeroconf service: {0}", e.ToString());
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private void OnApplicationStarted()
    {
        _logger.LogDebug("OnApplicationStarted");

        SyncMdns();
    }

    private void OnApplicationStopping()
    {
        _syncLock.Wait();
        try
        {
            _servicePublisher?.Dispose();
            _servicePublisher = null;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>
    /// Triggers on global configuration change.
    /// </summary>
    private void OnConfigurationUpdated(object? sender, EventArgs e)
    {
        _logger.LogDebug("OnConfigurationUpdated");

        SyncMdns();
    }

    /// <summary>
    /// Triggers on plugin configuration change.
    /// </summary>
    private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        _logger.LogDebug("OnConfigurationChanged");

        SyncMdns();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual async void Dispose(bool disposing)
    {
        if (disposing)
        {
            _servicePublisher?.Dispose();

            _sessionManager.SessionActivity -= OnSessionManagerSessionActivity;
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;
            _taskManager.TaskExecuting -= OnTaskExecuting;
            _taskManager.TaskCompleted -= OnTaskCompleted;

            _configurationManager.ConfigurationUpdated -= OnConfigurationUpdated;
            ConfigurationChanged -= OnConfigurationChanged;

            _startedCallback.Dispose();
            _shutdownCallback.Dispose();
            _syncLock.Dispose();

            await _sessionIdleInhibitor.DisposeAsync().ConfigureAwait(false);
            await _taskIdleInhibitor.DisposeAsync().ConfigureAwait(false);
        }
    }

    private struct MdnsConfig
    {
        public readonly bool Enabled;
        public readonly string FriendlyName;
        public readonly bool ListenWithHttps;
        public readonly ushort HttpPort;
        public readonly ushort HttpsPort;

        public MdnsConfig(bool enabled, string name, bool useHttps, int httpPort, int httpsPort)
        {
            Enabled = enabled;
            FriendlyName = name;
            ListenWithHttps = useHttps;
            HttpPort = (ushort)httpPort;
            HttpsPort = (ushort)httpsPort;
        }
    }
}
