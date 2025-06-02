using System.Threading.Tasks;

namespace Jellyfin.Plugin.Insomniac.Inhibitor;

interface IInhibitor
{
    Task Increment();

    Task Decrement();
}
