using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using CFStringRef = nint;

namespace Jellyfin.Plugin.Insomniac.Inhibitors;

/// <summary>
/// Signal inhibition using IOPM.
/// </summary>
public sealed class IOPMInhibitor : IIdleInhibitor
{

    private static CFStringRef kIOPMAssertionTypePreventUserIdleSystemSleep = CFStringCreate("PreventUserIdleSystemSleep");
    private static CFStringRef kIOPMAssertionTypeNetworkClientActive = CFStringCreate("NetworkClientActive");
    private static uint kIOPMAssertionLevelOn = 255;

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern CFStringRef CFStringCreateWithCharacters(
        /* CFAllocatorRef */ IntPtr alloc,
        /* const UniChar* */ [MarshalAs(UnmanagedType.LPWStr)] string chars,
        /* CFIndex */ long numChars);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void CFRelease(
        /* CFTypeRef */ IntPtr alloc);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int IOPMAssertionCreateWithName(
        /* CFStringRef */ CFStringRef assertionType,
        /* IOPMAssertionLevel */ uint assertionLevel,
        /* CFStringRef */ CFStringRef assertionName,
        /* IOPMAssertionID* */ out IntPtr assertionID);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int IOPMAssertionRelease(/* IOPMAssertionID* */ IntPtr AssertionID);

    private static IntPtr CFStringCreate(string value)
    {
        return CFStringCreateWithCharacters(IntPtr.Zero, value, value.Length);
    }

    async Task<Func<Task>> IIdleInhibitor.Inhibit(InhibitorType type, string reason)
    {
        CFStringRef cfReason = CFStringCreate(reason);
        IntPtr assertionId;

        try
        {
            CFStringRef assertionType = type == InhibitorType.NetworkClient ? kIOPMAssertionTypeNetworkClientActive : kIOPMAssertionTypePreventUserIdleSystemSleep;
            int res = IOPMAssertionCreateWithName(assertionType, kIOPMAssertionLevelOn, CFStringCreate(reason), out assertionId);
            if (res != 0)
            {
                throw new InvalidOperationException($"""Failed to create inhibitor, code={res}""");
            }
        }
        finally
        {
            CFRelease(cfReason);
        }

        return async () =>
        {
            _ = IOPMAssertionRelease(assertionId);
        };
    }
}
