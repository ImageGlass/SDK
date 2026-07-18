/*
ConsoleColorPicker – sample integrated external tool for ImageGlass v10.
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License

Register in igconfig.json under "Tools":
{
    "ToolId": "Tool_ConsoleColorPicker",
    "ToolName": "Console Color Picker",
    "Executable": "C:\\path\\to\\ConsoleColorPicker.exe",
    "Arguments": "",
    "IsIntegrated": true,
    "Hotkeys": [ "K" ]
}
*/
namespace ConsoleColorPicker;


internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Initialize the file logger FIRST so we always have a record, even
        // if everything else fails.
        Log.Init();
        Log.Write($"Main() entered. args = [{string.Join(", ", args)}]");

        // Best-effort: give the user a visible console on Windows.
        ConsoleHelper.TryAttach();

        try
        {
            using var tool = new ConsoleColorPickerTool
            {
                EnableDebug = args.Contains("--debug"),
                DebugLog = Log.Write,
            };
            Log.Write("Calling ToolBase.RunAsync...");
            await tool.RunAsync(args).ConfigureAwait(false);
            Log.Write("ToolBase.RunAsync returned.");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write($"FATAL: {ex}");
            if (args.Length == 0)
            {
                try
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("This tool must be launched by ImageGlass (it requires --pipe <name>).");
                    Console.Error.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                }
                catch { }
            }
            return 1;
        }
    }
}
