/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Text.Json;

namespace ImageGlass.SDK.Tools;

/// <summary>
/// Implements <see cref="IToolHostProxy"/> by forwarding each call
/// as a typed request/response pair over the named pipe.
/// </summary>
internal sealed class ToolHostProxy(ToolClient client) : IToolHostProxy
{
    public async Task<ToolColor> ReadPixelAsync(int x, int y)
    {
        var resp = await client.SendRequestAsync(MessageTypes.READ_PIXEL,
            new ReadPixelRequest { X = x, Y = y }).ConfigureAwait(false);

        var data = DeserializePayload<ReadPixelResponse>(resp);
        return data is null ? default : new ToolColor(data.R, data.G, data.B, data.A);
    }


    public async Task<PixelBuffer?> GetPixelBufferAsync(bool selectionOnly = false)
    {
        var resp = await client.SendRequestAsync(MessageTypes.GET_PIXEL_BUFFER,
            new GetPixelBufferRequest { SelectionOnly = selectionOnly }).ConfigureAwait(false);

        var data = DeserializePayload<GetPixelBufferResponse>(resp);
        if (data is null || string.IsNullOrEmpty(data.MmfPath)) return null;

        return new PixelBuffer(data.MmfPath, data.Width, data.Height, data.Stride, data.ColorType);
    }


    public async Task ReleasePixelBufferAsync(PixelBuffer buffer)
    {
        buffer.Dispose();
        await client.SendRequestAsync(MessageTypes.RELEASE_PIXEL_BUFFER,
            new ReleasePixelBufferRequest { MmfPath = buffer.FilePath }).ConfigureAwait(false);
    }


    public async Task<ToolPhotoMetadata?> GetPhotoMetadataAsync()
    {
        var resp = await client.SendRequestAsync(MessageTypes.GET_PHOTO_METADATA).ConfigureAwait(false);
        return DeserializePayload<ToolPhotoMetadata>(resp);
    }


    public async Task<ToolPhotoList> GetPhotoListAsync()
    {
        var resp = await client.SendRequestAsync(MessageTypes.GET_PHOTO_LIST).ConfigureAwait(false);
        return DeserializePayload<ToolPhotoList>(resp) ?? new ToolPhotoList();
    }


    public async Task<(int Width, int Height)> GetSourceSizeAsync()
    {
        var resp = await client.SendRequestAsync(MessageTypes.GET_SOURCE_SIZE).ConfigureAwait(false);
        var data = DeserializePayload<SourceSizeResponse>(resp);
        return data is null ? (0, 0) : (data.Width, data.Height);
    }


    public async Task<ToolRect?> GetSelectionAsync()
    {
        var resp = await client.SendRequestAsync(MessageTypes.GET_SELECTION).ConfigureAwait(false);
        var data = DeserializePayload<SetSelectionRequest>(resp);
        if (data?.X is null) return null;
        return new ToolRect(data.X.Value, data.Y ?? 0, data.Width ?? 0, data.Height ?? 0);
    }


    public async Task SetSelectionAsync(ToolRect? rect)
    {
        var payload = rect.HasValue
            ? new SetSelectionRequest { X = rect.Value.X, Y = rect.Value.Y, Width = rect.Value.Width, Height = rect.Value.Height }
            : new SetSelectionRequest();
        await client.SendRequestAsync(MessageTypes.SET_SELECTION, payload).ConfigureAwait(false);
    }


    public async Task EnableSelectionAsync(bool enable)
    {
        await client.SendRequestAsync(MessageTypes.ENABLE_SELECTION,
            new EnableSelectionRequest { Enable = enable }).ConfigureAwait(false);
    }


    public async Task RunApiAsync(string apiName, string? argument = null)
    {
        await client.SendRequestAsync(MessageTypes.RUN_API,
            new RunApiRequest { ApiName = apiName, Argument = argument }).ConfigureAwait(false);
    }


    public async Task<ThemeInfo> GetThemeInfoAsync()
    {
        var resp = await client.SendRequestAsync(MessageTypes.GET_THEME_INFO).ConfigureAwait(false);
        return DeserializePayload<ThemeInfo>(resp) ?? new ThemeInfo();
    }


    public async Task SubscribeEventsAsync(ToolEventSubscriptions subscriptions)
    {
        await client.SendRequestAsync(MessageTypes.SUBSCRIBE_EVENTS, subscriptions).ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Serializer uses source-generated ToolJsonContext options")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Serializer uses source-generated ToolJsonContext options")]
    private static T? DeserializePayload<T>(ToolMessage msg) where T : class
    {
        if (msg.Payload is null) return null;
        return msg.Payload.Value.Deserialize<T>(ToolJsonContext.Default.Options);
    }
}
