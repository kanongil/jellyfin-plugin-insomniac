using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus;

[assembly: InternalsVisibleTo(Tmds.DBus.Connection.DynamicAssemblyName)]

namespace Jellyfin.Plugin.Insomniac.Mdns;

/// <summary>
/// Register Bonjour/Zeroconf service using Avahi D-BUS API.
/// </summary>
public sealed class AvahiPublisher : IServicePublisher
{
    private const int AVAHI_SERVER_RUNNING = 2;

    private readonly SemaphoreSlim _transactionLock = new(1, 1);
    private Connection? _connection; // The life-time of the service registration is tied to the connection
    private IDisposable? _stateListener;
    private ServiceConfig _config;
    private int _savedState = -1;
    private IServer? _server;
    private IEntryGroup? _group;

    [DBusInterface("org.freedesktop.Avahi.Server")]
    public interface IServer : IDBusObject
    {
        Task<uint> GetAPIVersionAsync();

        Task<int> GetStateAsync();

        Task<ObjectPath> EntryGroupNewAsync();

        Task<IDisposable> WatchStateChangedAsync(Action<(int state, string error)> handler, Action<Exception> onError = null);
    }

    [DBusInterface("org.freedesktop.Avahi.EntryGroup")]
    public interface IEntryGroup : IDBusObject
    {
        /// <summary>
        /// Try to publish the services in the group.
        /// </summary>
        Task CommitAsync();

        /// <summary>
        /// Clear all services from group.
        /// </summary>
        Task ResetAsync();

        /// <summary>
        /// Add a service to the group.
        /// </summary>
        Task AddServiceAsync(int Interface, int Protocol, uint Flags, string Name, string Type, string Domain, string Host, ushort Port, byte[][] Txt);

        /// <summary>
        /// Add a subtype to a service.
        /// </summary>
        Task AddServiceSubtypeAsync(int Interface, int Protocol, uint Flags, string Name, string Type, string Domain, string Subtype);
    }

    private async Task<IServer> PrepareServer()
    {
        if (_server is null)
        {
            _connection = new Connection(Address.System);

            await _connection.ConnectAsync().ConfigureAwait(false);

            _server = _connection.CreateProxy<IServer>("org.freedesktop.Avahi", "/");

            // TODO: verify that the server API version is OK

            _stateListener = await _server.WatchStateChangedAsync(
                (update) => OnServerStateChanged(_server, update.state, update.error)).ConfigureAwait(false);
        }

        return _server;
    }

    private async Task<int> GetServerState()
    {
        var server = await PrepareServer().ConfigureAwait(false);
        if (_savedState == -1)
        {
            _savedState = await server.GetStateAsync().ConfigureAwait(false);
        }

        return _savedState;
    }

    public async Task PublishConfig(ServiceConfig config)
    {
        if (config == _config)
        {
            return;
        }

        _config = config;

        var state = await GetServerState().ConfigureAwait(false);
        if (state == AVAHI_SERVER_RUNNING)
        {
            await CommitConfig(config).ConfigureAwait(false);
        }
        else
        {
            // config is committed once Avahi is ready
        }
    }

    private async Task<IEntryGroup> GetEmptiedGroup(IServer server)
    {
        if (_group is null)
        {
            var groupPath = await server.EntryGroupNewAsync().ConfigureAwait(false);
            _group = _connection!.CreateProxy<IEntryGroup>("org.freedesktop.Avahi", groupPath);
        }
        else
        {
            await _group.ResetAsync().ConfigureAwait(false);
        }

        return _group;
    }

    /// <summary>
    /// Called once the Avahi deamon is ready, and when it is ready after a restart.
    /// </summary>
    private async Task CommitConfig(ServiceConfig? config)
    {
        await _transactionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (config is ServiceConfig c)
            {
                var group = await GetEmptiedGroup(_server!).ConfigureAwait(false);

                await group.AddServiceAsync(
                    -1,
                    -1,
                    0,
                    c.Name,
                    c.ServiceType,
                    string.Empty,
                    string.Empty,
                    c.Port,
                    Array.Empty<byte[]>()).ConfigureAwait(false);

                if (c.SubType is not null)
                {
                    var subType = c.SubType + "._sub." + c.ServiceType;
                    await group.AddServiceSubtypeAsync(
                        -1,
                        -1,
                        0,
                        c.Name,
                        c.ServiceType,
                        string.Empty,
                        subType).ConfigureAwait(false);
                }

                await group.CommitAsync().ConfigureAwait(false);
            }
            else if (_group is not null)
            {
                try
                {
                    // This fails if the Avahi daemon is restarted. Just ignore it.

                    await _group.ResetAsync().ConfigureAwait(false);
                    await _group.CommitAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Ignore
                }

                _group = null;
            }
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    private async Task OnServerStateChanged(IServer server, int state, string error)
    {
        _savedState = state;

        // This handles an initial bad state, as well as an Avahi daemon restart

        await CommitConfig(state == AVAHI_SERVER_RUNNING ? _config : null).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stateListener?.Dispose();
        _connection?.Dispose();
        _transactionLock.Dispose();
    }
}
#pragma warning restore CA2216
