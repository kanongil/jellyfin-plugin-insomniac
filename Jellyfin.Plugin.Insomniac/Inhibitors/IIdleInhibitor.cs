using System;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Insomniac.Inhibitors;

public enum InhibitorType
{
    SystemIdle,
    NetworkClient
}

internal interface IIdleInhibitor
{
    /// <summary>
    /// Initiate a device inhibition of specified type.
    /// </summary>
    /// <param name="type">Inhibition type.</param>
    /// <param name="reason">Description of why this inhibiting is active.</param>
    /// <returns>Async method called to release the inhibition.</returns>
    Task<Func<Task>> Inhibit(InhibitorType type, string reason);
}
