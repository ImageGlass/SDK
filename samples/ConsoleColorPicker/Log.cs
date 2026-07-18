/*
ConsoleColorPicker – sample integrated external tool for ImageGlass v10.
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ConsoleColorPicker;


/// <summary>
/// File-first logger. Writes are appended directly with synchronous flush so
/// nothing is lost if the process is killed or has no console attached.
/// </summary>
internal static class Log
{
    private static readonly Lock _gate = new();
    private static readonly string _logPath = Path.Combine(AppContext.BaseDirectory, "ConsoleColorPicker.log");

    public static void Init()
    {
        try { File.WriteAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] log started – pid {Environment.ProcessId}{Environment.NewLine}"); }
        catch { }
    }

    public static void Write(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        lock (_gate)
        {
            try { File.AppendAllText(_logPath, line + Environment.NewLine); } catch { }
            try { Console.WriteLine(line); } catch { }
        }
    }
}
