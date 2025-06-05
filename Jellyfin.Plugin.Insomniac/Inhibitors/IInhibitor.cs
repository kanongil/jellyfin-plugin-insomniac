using System;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Insomniac.Inhibitors;

internal interface IInhibitor
{
    Task<Func<Task>> Inhibit(string reason);
}
