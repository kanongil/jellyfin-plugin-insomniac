using System;
using System.Runtime.InteropServices;
using System.Threading;
using Jellyfin.Plugin.Insomniac.Platform;

namespace Jellyfin.Plugin.Insomniac.Mdns;

/// <summary>
/// Register Bonjour/Zeroconf service using CFNetwork.
/// </summary>
#pragma warning disable CA2216
public sealed class CFNetRegister : IDisposable
{
    private const string CFNetwork = "/System/Library/Frameworks/CFNetwork.framework/CFNetwork";
    private const int kCFNetServiceErrorCancel = -72005;

    private readonly string _serviceType;
    private readonly string _name;
    private readonly int _port;

    private IntPtr _service;
    private Thread _runner;

    public CFNetRegister(string serviceType, string name, int port)
    {
        _serviceType = serviceType;
        _name = name;
        _port = port;

        Publish();
    }

    [DllImport(CFNetwork, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern /* CFNetServiceRef */ IntPtr CFNetServiceCreate(
        /* CFAllocatorRef */ IntPtr alloc,
        /* CFStringRef */ IntPtr domain,
        /* CFStringRef */ IntPtr serviceType,
        /* CFStringRef */ IntPtr name,
        int port);

    [DllImport(CFNetwork, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern /* Boolean */ uint CFNetServiceRegisterWithOptions(
        /* CFNetServiceRef */ IntPtr theService,
        /* CFOptionFlags */ ulong options,
        /* CFStreamError* */ IntPtr error);

    [DllImport(CFNetwork, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void CFNetServiceCancel(
        /* CFNetServiceRef */ IntPtr theService);

    private void Publish()
    {
        // Prepare _service

        if (_service != 0)
        {
            return;
        }

        var cfDomain = MacOS.CFStringCreate(string.Empty);
        var cfServiceType = MacOS.CFStringCreate(_serviceType);
        var cfName = MacOS.CFStringCreate(_name);
        try
        {
            _service = CFNetServiceCreate(IntPtr.Zero, cfDomain, cfServiceType, cfName, _port);
            if (_service == 0)
            {
                throw new ArgumentException("CFNetServiceCreate() failed");
            }
        }
        finally
        {
            MacOS.CFRelease(cfDomain);
            MacOS.CFRelease(cfServiceType);
            MacOS.CFRelease(cfName);
        }

        // Start thread that does the register call

        _runner = new Thread(new ThreadStart(() =>
        {
            try
            {
                MacOS.CFStreamError error;
                error.domain = 0;
                error.error = 0;

                IntPtr pnt = Marshal.AllocHGlobal(Marshal.SizeOf(error));
                Marshal.StructureToPtr(error, pnt, false);

                // The CFNetServiceRegisterWithOptions() call is blocking and only returns
                // when an error occurs, or if CFNetServiceCancel() was called on it.

                bool res = CFNetServiceRegisterWithOptions(_service, 0, pnt) != 0;
                error = (MacOS.CFStreamError)Marshal.PtrToStructure(pnt, typeof(MacOS.CFStreamError))!;
                if (res == false && error.error != kCFNetServiceErrorCancel)
                {
                    Console.WriteLine("CFNetServiceRegisterWithOptions() failed with error code={0}", error.error);
                }
            }
            finally
            {
                MacOS.CFRelease(_service);
                _service = 0;
            }
        }));

        _runner.Name = "CFNetServiceRegister()";
        _runner.Start();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var service = _service;
        if (service != 0)
        {
            // This should make the blocking CFNetServiceRegisterWithOptions() stop running,
            // release the _service, and stop the thread.
            CFNetServiceCancel(service);
            _runner.Join();
        }

        GC.SuppressFinalize(this);
    }
}
#pragma warning restore CA2216
