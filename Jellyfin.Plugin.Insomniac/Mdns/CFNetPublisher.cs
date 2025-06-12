using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Insomniac.Platform;

namespace Jellyfin.Plugin.Insomniac.Mdns;

/// <summary>
/// Register Bonjour/Zeroconf service using CFNetwork.
/// </summary>
#pragma warning disable CA2216
public sealed class CFNetPublisher : IServicePublisher
{
    private const string CFNetwork = "/System/Library/Frameworks/CFNetwork.framework/CFNetwork";
    private const int kCFNetServiceErrorCancel = -72005;

    private Action? _cancelAction;

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

    public async Task PublishConfig(ServiceConfig config)
    {
        // Teardown any current service

        _cancelAction?.Invoke();

        // Publish new config

        Publish(config);
    }

    private void Publish(ServiceConfig config)
    {
        // Prepare service

        string type = config.SubType is not null ? $"{config.ServiceType},{config.SubType}" : config.ServiceType;

        var cfDomain = MacOS.CFStringCreate(string.Empty);
        var cfServiceType = MacOS.CFStringCreate(type);
        var cfName = MacOS.CFStringCreate(config.Name);
        IntPtr service;
        try
        {
            service = CFNetServiceCreate(IntPtr.Zero, cfDomain, cfServiceType, cfName, config.Port);
            if (service == 0)
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

        var runner = new Thread(new ThreadStart(() =>
        {
            MacOS.CFStreamError error;
            error.domain = 0;
            error.error = 0;

            IntPtr pnt = Marshal.AllocHGlobal(Marshal.SizeOf(error));
            Marshal.StructureToPtr(error, pnt, false);

            // The CFNetServiceRegisterWithOptions() call is blocking and only returns
            // when an error occurs, or if CFNetServiceCancel() was called on it.

            bool res = CFNetServiceRegisterWithOptions(service, 0, pnt) != 0;
            error = (MacOS.CFStreamError)Marshal.PtrToStructure(pnt, typeof(MacOS.CFStreamError))!;
            if (!res && error.error != kCFNetServiceErrorCancel)
            {
                Console.WriteLine("CFNetServiceRegisterWithOptions() failed with error code={0}", error.error);
            }
        }));

        runner.Name = "CFNetServiceRegister()";
        runner.Start();

        // Register cancel action

        _cancelAction = () =>
        {
            _cancelAction = null;

            // This should make the blocking CFNetServiceRegisterWithOptions() stop running,
            // release the service, and stop the thread.
            CFNetServiceCancel(service);

            runner.Join();

            MacOS.CFRelease(service);
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _cancelAction?.Invoke();
    }
}
#pragma warning restore CA2216
