using System;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Insomniac.Mdns;

internal interface IServicePublisher : IDisposable
{
    public Task PublishConfig(ServiceConfig config);
}

public struct ServiceConfig
{
    public ServiceConfig(string serviceType, string? subType, string name, ushort port)
    {
        ServiceType = serviceType;
        SubType = subType;
        Name = name;
        Port = port;
    }

    public string ServiceType { get; private set; }

    public string? SubType { get; private set; }

    public string Name { get; private set; }

    public ushort Port { get; private set; }

    public static bool operator ==(ServiceConfig left, ServiceConfig right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ServiceConfig left, ServiceConfig right)
    {
        return !(left == right);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is ServiceConfig config)
        {
            return config.ServiceType == ServiceType &&
                    config.SubType == SubType &&
                    config.Name == Name &&
                    config.Port == Port;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;

        hash.Add(ServiceType);
        hash.Add(SubType);
        hash.Add(Name);
        hash.Add(Port);

        return hash.ToHashCode();
    }
}
