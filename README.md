# Jellyfin Insomniac Idle Inhibitor plugin

Insomniac is a Jellyfin plugin that signals the host system to not enter idle sleep while
it is actively used. This means that hosts can be configured to go to idle sleep with less compromise, saving electicity.

For this feature to be useful, a mechanism (like Wake-on-Lan) to remotely wake the server
is probably required. It works exceptionally on networks and hosts that support
[Bonjour Sleep Proxy](https://en.wikipedia.org/wiki/Bonjour_Sleep_Proxy), where the Jellyfin
server is registered as a service. When properly configured, it allows the server to automagically
wake on any connection request to the Jellyfin port, even from clients on remote networks.
To help with this, the plugin can be [configured to automatically announce the service using mDNS](#announce-server-using-mdns).

## Features

- Prevents system idle sleep during remote playback
- Prevents system idle sleep while running scheduled tasks
- Optional mDNS (a.k.a. Bonjour / Zeroconf) service registration
- Configurable idle inhibition removal delay

## Requirements

- The host system must be one of:
  - **Linux** with `systemd` or `elogind`
  - **macOS**
  - Note that **Windows** is *not currently supported*

- The host should be configured to enter *system sleep* after an inactivity timeout (idle sleep).

- The users should have a mechanism to remotely wake the server.

## Limitations

- Does not wake the server again. Neither for scheduled timers, nor for network requests.
  - *Note that the network request wake limitation can be mitigated using the mDNS service service announcement
  on networks that support [Bonjour Sleep Proxy](https://en.wikipedia.org/wiki/Bonjour_Sleep_Proxy)*.
- Live TV scheduled recordings are not supported, and could be missed if the host is sleeping.
- Clients can provide limited interaction signals while not actively consuming content, causing the inhibition removal delay to trigger.

## Installation

The main way to install the plugin is from this GitHub repository, alternatively
you can build it from source.

### Github Releases

1. **Download the Plugin:**
   - Go to the latest release on GitHub [here](https://github.com/kanongil/jellyfin-plugin-insomniac/releases/latest).
   - Download the `insomniac_*.zip` file.

1. **Extract and Place Files:**
   - Extract all `.dll` files and `meta.json` from the zip file.
   - Put them in a folder named `Insomniac`.
   - Copy this `Insomniac` folder to the `plugins` folder in your Jellyfin program
     data directory or inside the Jellyfin install directory. For help finding
     your Jellyfin install location, check the "Data Directory" section on
     [this page](https://jellyfin.org/docs/general/administration/configuration.html).

1. **Restart Jellyfin:**
   - Start or restart your Jellyfin server to apply the changes.

### Build Process

1. **Clone or Download the Repository:**
   - Clone or download the repository from GitHub.

1. **Set Up .NET Core SDK:**
   - Make sure you have the .NET Core SDK installed on your computer.

1. **Build the Plugin:**
   - Open a terminal and navigate to the repository directory.
   - Run the following commands to restore and publish the project:

     ```sh
     $ dotnet restore Jellyfin.Plugin.Insomniac/Jellyfin.Plugin.Insomniac.csproj
     $ dotnet publish -c Release Jellyfin.Plugin.Insomniac/Jellyfin.Plugin.Insomniac.csproj
     ```

1. **Copy Built Files:**
   - After building, go to the `bin/Release/net8.0/publish` directory.
   - Copy the `Jellyfin.Plugin.Insomniac.dll` and `Tmds.DBus.dll` files to a folder named `Insomniac`.
   - Place this `Insomniac` folder in the `plugins` directory of your Jellyfin
     program data directory or inside the portable install directory. For help
     finding your Jellyfin install location, check the "Data Directory" section
     on [this page](https://jellyfin.org/docs/general/administration/configuration.html).

1. **Restart Jellyfin:**
   - Start or restart your Jellyfin server to apply the changes.

## Configuration

The plugin can be configured by *server managers* from the `Administration` -> `Dashboard` -> `My Plugins` -> `Insomniac` page.

### ActivityIdleDelaySeconds

The main property to configure, is `ActivityIdleDelaySeconds`, which is delay that the
plugin will wait on each detected user session acticity, before it removes any idle
inhibition. Increase this value to give users more time to be inactive, without
inadvertently sleeping. Reduce the value to potentially sleep sooner, and save power.

Note that the system won't automatically enter sleep after the timeout, and the actual
delay will depend on the host system sleep delay, and current activity pattern.

### Only inhibit idle for remote sessions

The `Only inhibit idle for remote sessions` toggle is probably best left enabled. It toggles
whether to inhibit sleep for sessions coming from `localhost` and the host ip address. These
local sessions should normally apply their own sleep inhibition, that also applies to the
monitor, in the player itself. As such Jellyfin should not need to apply any extra
inhibitions to keep the host awake.

### Announce server using mDNS

This is a very important checkbox, which helps enable the service *Wake on Demand* feature.

Enabling the checkbox makes Jellyfin announce its presence on the local network as a service
using mDNS (a.k.a. Bonjour / Zeroconf). Specifically using the `_http._tcp` or `_https._tcp`
service type, with a subtype of `_jellyfin`. The service name will be the configured name of
the Jellyfin server.
It is disabled by default to not unintentionally announce the presence of the server on the
local network.

While this feature allows clients to discover the server, the main reason to enable it, is
to support *Wake on Demand*, allowing the server to wake on connection requests to the
server url. To fully enable this feature, additional setup might be required, as follows:

- On **macOS** systems it should only need [Wake for network access](https://support.apple.com/en-gb/guide/mac-help/mh27905/mac)
to be enabled in the system settings.

- On **Linux** systems it will need to be combined with:

    1. A network interface and driver that supports, and is configured for, Wake-on-LAN.
    1. An install of [Avahi](https://avahi.org), included in many default distro installations.
    1. A network local, always-on, device that supports the
    [Bonjour Sleep Proxy](https://en.wikipedia.org/wiki/Bonjour_Sleep_Proxy) service, like an Apple TV.
    1. A host configuration that reports sleep status and services to the proxy using something
    like [SleepProxyClient](https://github.com/awein/SleepProxyClient).
