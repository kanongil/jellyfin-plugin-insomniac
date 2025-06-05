using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tmds.DBus;

[assembly: InternalsVisibleTo(Tmds.DBus.Connection.DynamicAssemblyName)]

namespace Jellyfin.Plugin.Insomniac.Inhibitors;

/// <summary>
/// Signal inhibition using D-BUS org.freedesktop.login1.Manager.Inhibit().
/// </summary>
public sealed class DbusLoginManagerInhibitor : IInhibitor
{
    private const string Who = "Jellyfin.Plugin.Insomniac";

    [DBusInterface("org.freedesktop.login1.Manager")]
    public interface ILoginManager : IDBusObject
    {
        Task<CloseSafeHandle> InhibitAsync(string what, string who, string why, string mode);
    }

    private async Task<SafeHandle?> DoDbusInhibit(string reason)
    {
        Connection connection = new Connection(Address.System);

        await connection.ConnectAsync().ConfigureAwait(false);

        var proxy = connection.CreateProxy<ILoginManager>("org.freedesktop.login1", "/org/freedesktop/login1");

        return await proxy.InhibitAsync("idle", Who, reason, "block").ConfigureAwait(false);
    }

    async Task<Func<Task>> IInhibitor.Inhibit(string reason)
    {
        var inhibitFd = await DoDbusInhibit(reason).ConfigureAwait(false);
        return async () =>
        {
            inhibitFd!.Close();
        };
    }
}
