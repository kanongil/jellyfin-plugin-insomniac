using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Insomniac.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // set default options here
        OnlyInhibitRemote = true;
        EnableMdns = false;
        ActivityIdleDelaySeconds = 120;
    }

    /// <summary>
    /// Gets or sets an the activity idle delay setting.
    /// </summary>
    public int ActivityIdleDelaySeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether only remote sessions inhibit idle.
    /// </summary>
    public bool OnlyInhibitRemote { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether host is announced using Bonjour / Zeroconf.
    /// </summary>
    public bool EnableMdns { get; set; }
}
