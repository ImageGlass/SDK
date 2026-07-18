/*
ConsoleColorPicker – sample integrated external tool for ImageGlass v10.
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License

Behavior:
- On launch (OnExecute): logs metadata of the current photo.
- On photo change in ImageGlass: logs metadata of the new photo.
- On click in the ImageGlass viewer: logs the RGBA value of the clicked pixel.
*/
using ImageGlass.SDK.Tools;

namespace ConsoleColorPicker;


/// <summary>
/// Sample tool that logs photo metadata and the color of clicked pixels.
/// </summary>
internal sealed class ConsoleColorPickerTool : ToolBase
{
    public override string ToolId => "Tool_ConsoleColorPicker";


    protected override async Task OnInitializedAsync()
    {
        Log.Write("============================================");
        Log.Write(" ConsoleColorPicker – connected to ImageGlass");
        Log.Write("============================================");
        Log.Write($"DataDirectory: {DataDirectory}");

        // Subscribe to pointer-pressed events so we can read the pixel under the click.
        try
        {
            await HostApi.SubscribeEventsAsync(new ToolEventSubscriptions
            {
                PointerPressed = true,
            }).ConfigureAwait(false);
            Log.Write("Subscribed to PointerPressed events.");
        }
        catch (Exception ex)
        {
            Log.Write($"SubscribeEventsAsync failed: {ex}");
        }
    }


    protected override async Task OnExecuteAsync(CancellationToken ct)
    {
        Log.Write("[EXECUTE] User opened the tool.");
        await PrintCurrentPhotoAsync().ConfigureAwait(false);
    }


    protected override void OnPhotoChanged(PhotoChangedEventArgs e)
    {
        // Quick info from the event itself, then fetch full metadata in the background.
        Log.Write("[PHOTO CHANGED]");
        if (string.IsNullOrEmpty(e.FilePath))
        {
            Log.Write("  (no photo loaded)");
            return;
        }

        Log.Write($"  File:    {e.FilePath}");
        Log.Write($"  Size:    {e.Width} x {e.Height} px");
        Log.Write($"  Format:  {e.Format ?? "(unknown)"}");
        Log.Write($"  Frames:  {e.FrameCount}{(e.CanAnimate ? " (animated)" : "")}");

        // Fire-and-forget the richer metadata fetch; isolate exceptions.
        _ = Task.Run(async () =>
        {
            try { await PrintCurrentPhotoAsync().ConfigureAwait(false); }
            catch (Exception ex) { Log.Write($"  Failed to read metadata: {ex}"); }
        });
    }


    protected override void OnPointerPressed(PointerEventArgs e)
    {
        var x = (int)Math.Round(e.SourceX);
        var y = (int)Math.Round(e.SourceY);

        // Fire-and-forget the host call; isolate exceptions so async-void doesn't crash dispatch.
        _ = Task.Run(async () =>
        {
            try
            {
                var color = await HostApi.ReadPixelAsync(x, y).ConfigureAwait(false);
                var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
                Log.Write($"[CLICK] ({x}, {y})  RGBA = ({color.R}, {color.G}, {color.B}, {color.A})  {hex}");
            }
            catch (Exception ex)
            {
                Log.Write($"[CLICK] Failed to read pixel at ({x}, {y}): {ex}");
            }
        });
    }


    protected override Task OnShutdownAsync()
    {
        Log.Write("ConsoleColorPicker shutting down. Bye!");
        return Task.CompletedTask;
    }


    private async Task PrintCurrentPhotoAsync()
    {
        ToolPhotoMetadata? meta;
        try
        {
            meta = await HostApi.GetPhotoMetadataAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Write($"GetPhotoMetadataAsync failed: {ex}");
            return;
        }

        if (meta is null)
        {
            Log.Write("  (no photo loaded)");
            return;
        }

        Log.Write($"  File:    {meta.FilePath}");
        Log.Write($"  Size:    {meta.Width} x {meta.Height} px");
        if (meta.Width != meta.OriginalWidth || meta.Height != meta.OriginalHeight)
        {
            Log.Write($"  Source:  {meta.OriginalWidth} x {meta.OriginalHeight} px");
        }
        Log.Write($"  Format:  {meta.Format ?? "(unknown)"}");
        Log.Write($"  Frames:  {meta.FrameCount}{(meta.CanAnimate ? " (animated)" : "")}");
        Log.Write($"  Alpha:   {(meta.HasAlpha ? "yes" : "no")}");
        Log.Write($"  Bytes:   {meta.FileSizeInBytes:N0}");
        if (!string.IsNullOrEmpty(meta.ColorProfileName))
        {
            Log.Write($"  Profile: {meta.ColorProfileName}");
        }
    }
}
