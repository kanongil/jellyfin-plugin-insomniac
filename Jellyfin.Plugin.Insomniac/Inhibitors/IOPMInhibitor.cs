using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Jellyfin.Plugin.Insomniac.Platform;

using CFStringRef = nint;

namespace Jellyfin.Plugin.Insomniac.Inhibitors;

/// <summary>
/// Signal inhibition using IOPM.
/// </summary>
public sealed class IOPMInhibitor : IIdleInhibitor
{
    private static readonly CFStringRef _kIOPMAssertionTypePreventUserIdleSystemSleep = MacOS.CFStringCreate("PreventUserIdleSystemSleep");
    private static readonly CFStringRef _kIOPMAssertionTypeNetworkClientActive = MacOS.CFStringCreate("NetworkClientActive");
    private const uint _kIOPMAssertionLevelOn = 255;

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int IOPMAssertionCreateWithName(
        /* CFStringRef */ CFStringRef assertionType,
        /* IOPMAssertionLevel */ uint assertionLevel,
        /* CFStringRef */ CFStringRef assertionName,
        /* IOPMAssertionID* */ out IntPtr assertionID);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int IOPMAssertionRelease(/* IOPMAssertionID* */ IntPtr assertionID);

    async Task<Func<Task>> IIdleInhibitor.Inhibit(InhibitorType type, string reason)
    {
        CFStringRef cfReason = MacOS.CFStringCreate(reason);
        IntPtr assertionId;

        try
        {
            CFStringRef assertionType = type == InhibitorType.NetworkClient ? _kIOPMAssertionTypeNetworkClientActive : _kIOPMAssertionTypePreventUserIdleSystemSleep;
            int res = IOPMAssertionCreateWithName(assertionType, _kIOPMAssertionLevelOn, MacOS.CFStringCreate(reason), out assertionId);
            if (res != 0)
            {
                throw new InvalidOperationException($"""Failed to create inhibitor, code={res}""");
            }
        }
        finally
        {
            MacOS.CFRelease(cfReason);
        }

        return async () =>
        {
            _ = IOPMAssertionRelease(assertionId);
        };
    }
}
