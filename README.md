# ScreenTranslator

A Windows overlay that translates on-screen Japanese, Chinese or Korean text in real time.
Drag a box around any part of the screen — a game's dialogue window, a video's subtitles, an app
without localisation — and English translations are drawn on top of the original text.

It uses the OCR engine built into Windows (offline, free) and the [DeepL API](https://www.deepl.com/pro-api)
for translation.

## Features

- **Region capture** — select any area of the primary monitor; only that area is read.
- **Change detection** — OCR and translation run only when the pixels in the region actually change.
- **Batched, cached translation** — every line in a frame goes to DeepL in one request; repeated lines come from a local cache.
- **Click-through overlay** — the app underneath stays fully usable, and the overlay is hidden from screen capture so it never re-reads its own output.
- **Tray icon and global hotkeys** for everything, with on-screen feedback.

## Requirements

- Windows 10 version 2004 (build 19041) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build (the .NET 10 Desktop Runtime to run)
- A DeepL API key — the [free plan](https://www.deepl.com/pro-api) (500,000 characters/month) is enough
- The Windows OCR pack for each language you want to read

### Installing OCR languages

**Settings › Time & language › Language & region › Add a language**, pick Japanese / Chinese (Simplified) /
Korean, and make sure **Optical character recognition** is ticked. Or, from an elevated PowerShell:

```powershell
Add-WindowsCapability -Online -Name "Language.OCR~~~ja-JP~0.0.1.0"   # Japanese
Add-WindowsCapability -Online -Name "Language.OCR~~~zh-CN~0.0.1.0"   # Chinese (Simplified)
Add-WindowsCapability -Online -Name "Language.OCR~~~ko-KR~0.0.1.0"   # Korean
```

The app tells you through a tray notification if the selected language's OCR pack is missing.

## Build and run

```powershell
git clone <this repo>
cd ScreenTranslator
dotnet run --project ScreenTranslator
```

On first launch you're asked for your DeepL API key. Then drag a box over the text you want translated.

To produce a standalone build:

```powershell
dotnet publish ScreenTranslator -c Release -r win-x64 --self-contained false -o publish
```

## Usage

| Hotkey | Action |
| --- | --- |
| `Ctrl+Shift+R` | Reselect the region (`Esc` keeps the current one) |
| `Ctrl+Shift+P` | Pause / resume (the region frame turns grey while paused) |
| `Ctrl+Shift+O` | Cycle overlay opacity (100 → 75 → 50 → 25 %) |
| `Ctrl+Shift+1` | Source language: Japanese |
| `Ctrl+Shift+2` | Source language: Chinese (Simplified) |
| `Ctrl+Shift+3` | Source language: Korean |

Everything is also in the tray icon's right-click menu, including **Set DeepL API key…** and **Exit**.

## Configuration

Settings are stored in `%AppData%\ScreenTranslator\config.json` and saved automatically:

```json
{
  "DeepLApiKey": "",
  "SourceLanguage": "ja",
  "TargetLanguage": "EN-US",
  "OverlayOpacity": 0.75,
  "CaptureIntervalMs": 500
}
```

- `TargetLanguage` accepts any [DeepL target code](https://developers.deepl.com/docs/resources/supported-languages) (`EN-GB`, `DE`, `ES`, …).
- `CaptureIntervalMs` is how often the region is checked for changes (minimum 200).
- The `DEEPL_API_KEY` environment variable, if set, overrides the saved key.
- Free-plan keys (ending in `:fx`) and Pro keys are both detected automatically.

## How it works

```
DispatcherTimer ─▶ CopyFromScreen ─▶ pixels changed? ─no─▶ skip
                                          │ yes
                                          ▼
                     Windows.Media.Ocr (per-line text + boxes)
                                          ▼
                  DeepL /v2/translate (one batched request, cached)
                                          ▼
                 overlay labels drawn at each line's position
```

| File | Role |
| --- | --- |
| `App.xaml(.cs)` | Startup, single instance, tray menu, notifications |
| `MainWindow.xaml(.cs)` | Region selection, capture loop, overlay drawing, hotkeys |
| `ScreenCapture.cs` | Screen grab and change detection |
| `OcrHelper.cs` | Windows OCR, line bounds, CJK word joining |
| `TranslationHelper.cs` | DeepL client with batching, caching and error messages |
| `AppConfig.cs` | `config.json` load/save |
| `Languages.cs` | Supported source languages and their hotkey order |
| `ApiKeyWindow.xaml(.cs)` | DeepL key dialog |
| `NativeMethods.cs` | Win32 interop (window styles, hotkeys, capture exclusion) |

## Limitations

- Primary monitor only.
- Horizontal text only; vertical Japanese isn't laid out correctly.
- Very small fonts (under ~10 px) OCR poorly.

## Contributing

See [`model.md`](model.md) for how tasks in this repo are split between AI models and agents, and
[`graphify.md`](graphify.md) for the code knowledge graph used to navigate the codebase.
