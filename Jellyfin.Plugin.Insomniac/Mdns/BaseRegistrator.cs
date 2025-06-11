using System;

namespace Jellyfin.Plugin.Insomniac.Mdns;

public abstract class BaseRegistrator : IDisposable
{
    private bool _disposedValue;

    protected BaseRegistrator(string serviceType, string name, int port)
    {
        ServiceType = serviceType;
        Name = name;
        Port = port;
    }

    public string ServiceType { get; private set; }

    public string Name { get; private set; }

    public int Port { get; private set; }

    /// <summary>
    /// Method called when disposing the registration.
    /// </summary>
    protected abstract void Unpublish();

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                Unpublish();
            }

            _disposedValue = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
