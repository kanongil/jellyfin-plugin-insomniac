using System;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.Insomniac.Platform;

public sealed class MacOS
{
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern IntPtr CFStringCreateWithCharacters(
        /* CFAllocatorRef */ IntPtr alloc,
        /* const UniChar* */ [MarshalAs(UnmanagedType.LPWStr)] string chars,
        /* CFIndex */ long numChars);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "CFRelease")]
    private static extern void CFReleaseInternal(
        /* CFTypeRef */ IntPtr alloc);

    public static IntPtr CFStringCreate(string value)
    {
        return CFStringCreateWithCharacters(IntPtr.Zero, value, value.Length);
    }

    public static void CFRelease(IntPtr alloc)
    {
        CFReleaseInternal(alloc);
    }
}
