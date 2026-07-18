# ConsoleColorPicker

A minimal **integrated external tool** sample for ImageGlass v10.
It is a plain `dotnet` console app that connects back to the host through the
SDK's named-pipe channel and reacts to viewer events.

## What it does

| When                                              | What it logs                              |
| ------------------------------------------------- | ----------------------------------------- |
| The host runs the tool (`OnExecuteAsync`)         | Metadata of the current photo             |
| The user navigates to a different photo           | Metadata of the new photo                 |
| The user clicks in the ImageGlass viewer          | `RGBA` value of the pixel under the click |

### Where the output goes

ImageGlass is a GUI process, so a child launched with `UseShellExecute=false`
inherits no console – `Console.WriteLine` alone would go nowhere. This sample
therefore writes every line to a **log file** next to the executable:

```
ConsoleColorPicker\bin\Debug\net10.0\ConsoleColorPicker.log
```

On Windows it also tries to `AllocConsole` so you can watch the output live in a
window. Pass `--debug` to additionally emit the SDK's IPC lifecycle traces to the
same log.

## Build & run

```pwsh
dotnet build .\ConsoleColorPicker.csproj -c Debug
```

The compiled exe lives in `bin\Debug\net10.0\ConsoleColorPicker.exe`.

> The tool **must** be launched by ImageGlass – it expects a `--pipe <name>`
> argument. Running the exe directly logs a fatal error and exits with a
> non-zero code.

## Register the tool in `igconfig.json`

Add an entry under `Tools`:

```json
"Tools": [
	{
		"ToolId": "Tool_ConsoleColorPicker",
		"ToolName": "Console Color Picker",
		"Executable": "D:\\path\\to\\ConsoleColorPicker.exe",
		"Arguments": "",
		"IsIntegrated": true,
		"Hotkeys": ["K"]
	}
]
```

`ToolId` in the config **must** match `ConsoleColorPickerTool.ToolId` in code.
`IsIntegrated` must be `true` so the host launches the process with `--pipe`
and wires up the `HostApi` proxy.

## How it works (SDK surface used)

- Subclasses `ImageGlass.SDK.Tools.ToolBase`.
- Overrides `OnInitializedAsync` to subscribe to pointer-pressed events via
		`HostApi.SubscribeEventsAsync(new ToolEventSubscriptions { PointerPressed = true })`.
- Overrides `OnExecuteAsync` and `OnPhotoChanged` to call
		`HostApi.GetPhotoMetadataAsync()`.
- Overrides `OnPointerPressed` to call `HostApi.ReadPixelAsync(x, y)` using
		the source-image coordinates from `PointerEventArgs.SourceX/Y`.
- Overrides `OnShutdownAsync` to log a final message when the host disconnects.
