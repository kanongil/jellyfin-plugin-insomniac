using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tmds.DBus;

[assembly: InternalsVisibleTo(Tmds.DBus.Connection.DynamicAssemblyName)]

namespace Jellyfin.Plugin.Insomniac.Inhibitor;

// TODO: handle two types of inhibition - remote access / task

public sealed class DbusLoginManagerInhibitor : IInhibitor
{
    private int _active = 0;

    private SafeHandle? _inhibitFd = null;

    [DBusInterface("org.freedesktop.login1.Manager")]
    public interface ILoginManager : IDBusObject
    {
        Task<CloseSafeHandle> InhibitAsync(string what, string who, string why, string mode);
    }

    public DbusLoginManagerInhibitor()
    {
    }

    private async Task<SafeHandle?> DoDbusInhibit()
    {
        string? systemBusAddress = Address.System;
        if (systemBusAddress is null)
        {
            // Console.Write("Can not determine system bus address");  // TODO: error!
            return null;
        }

        Connection connection = new Connection(Address.System!);
        await connection.ConnectAsync().ConfigureAwait(false);
        // Console.WriteLine("Connected to system bus.");

        var proxy = connection.CreateProxy<ILoginManager>("org.freedesktop.login1", "/org/freedesktop/login1");

        return await proxy.InhibitAsync("idle", "Jellyfin.Plugin.Insomniac", "Busy", "block").ConfigureAwait(false);
    }

    async Task IInhibitor.Increment()
    {
        _active++;

        if (_active == 1)
        {
            _inhibitFd = await DoDbusInhibit().ConfigureAwait(false);  // TODO: handle race!!
        }
    }

    async Task IInhibitor.Decrement()
    {
        _active--;

        if (_active == 0)
        {
            _inhibitFd!.Close();
            _inhibitFd = null;
        }
    }
}
