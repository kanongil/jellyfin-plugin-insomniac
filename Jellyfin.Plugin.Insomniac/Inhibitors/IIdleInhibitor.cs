using System;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Insomniac.Inhibitors;

internal interface IIdleInhibitor
{
    /// <summary>
    /// Initiate a system idle inhibition.
    /// </summary>
    /// <param name="reason">Description of why this inhibiting is active.</param>
    /// <returns>Async method called to release the inhibition.</returns>
    Task<Func<Task>> Inhibit(string reason);
}
