/*
ConsoleColorPicker – sample integrated external tool for ImageGlass v10.
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ConsoleColorPicker;


/// <summary>
/// Best-effort console attachment for a GUI-launched child process.
/// <para>
/// ImageGlass is a GUI process; a child started with <c>UseShellExecute=false</c>
/// inherits no stdout, so <see cref="Console.WriteLine(string)"/> silently goes
/// nowhere. On Windows we allocate our own console window so the user can watch
/// live output. All output is also mirrored to <see cref="Log"/>.
/// </para>
/// </summary>
internal static class ConsoleHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();


    /// <summary>
    /// Tries to allocate and wire up a visible console window on Windows.
    /// No-op on other platforms. Never throws.
    /// </summary>
    public static void TryAttach()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            if (AllocConsole())
            {
                var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                Console.SetOut(stdout);
                Console.SetError(stderr);
                Console.Title = "ConsoleColorPicker – ImageGlass tool";
                Log.Write("AllocConsole succeeded.");
            }
            else
            {
                Log.Write($"AllocConsole returned false (LastError = {Marshal.GetLastWin32Error()}).");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"AllocConsole threw: {ex}");
        }
    }
}
