# ScreenTranslator

A free Windows overlay that translates on-screen Japanese, Chinese or Korean text in real time.
Drag a box around any part of any screen (a game's dialogue window, a video's subtitles, a manga or
novel page, an app without localisation) and translations are drawn right on top of the original text.

Text is read with the OCR engine built into Windows (offline, free) and translated with Google Translate
out of the box, with no account or key needed. You can switch to DeepL or Google Cloud with your own key.

## Features

- **Works out of the box**: free Google translation with no signup. Bring your own DeepL or Google Cloud key if you want.
- **Any monitor**: select a region on any screen, including monitors with different display scaling.
- **Horizontal and vertical text**: vertical Japanese and Chinese (novels, manga) is read column by column, right to left.
- **Whole sentences**: lines and columns that wrap are joined before translating, so the translator sees full sentences instead of fragments.
- **Change detection**: text is only read once it has *settled*, so typewriter-style text isn't translated half-written.
- **Labels that never overlap**: each translation covers its source text and shrinks to fit. The text is outlined like subtitles, so it stays readable at any background opacity, down to 0 % (text only).
- **Adjustable**: background opacity, text size (50–200 %), target language, capture speed.
- **Peek**: hold a hotkey to hide the translations and see the original.
- **Game bar**: an optional Xbox Game Bar–style panel with Translator, Glossary and Settings widgets you can drag around and pin.
- **Glossary** (DeepL): character names and game terms always translate the way you choose, with correct grammar around them.
- **Custom hotkeys**: every hotkey can be changed or turned off, so none clash with your other apps.
- **Click-through and invisible to capture**: the app underneath stays fully usable, and the overlay never re-reads its own output.
- **Log file** for bug reports, with no screen text, translations or keys in it.

## Download

Get the latest version from the [Releases](../../releases) page:

| File | For |
| --- | --- |
| `ScreenTranslator-x.y.z-win-x64.zip` | Most people: one `.exe`, nothing else to install |
| `ScreenTranslator-x.y.z-win-x64-needs-dotnet10.zip` | Smaller download if you already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

Unzip, run `ScreenTranslator.exe`, and drag a box over the text you want translated.

## Requirements

- Windows 10 version 2004 (build 19041) or Windows 11
- The Windows OCR pack for each language you want to read (see below)

### Installing OCR languages

**Settings › Time & language › Language & region › Add a language**, pick the language, and make sure
**Optical character recognition** is ticked. Or, from an elevated PowerShell:

```powershell
Add-WindowsCapability -Online -Name "Language.OCR~~~ja-JP~0.0.1.0"   # Japanese
Add-WindowsCapability -Online -Name "Language.OCR~~~zh-CN~0.0.1.0"   # Chinese (Simplified)
Add-WindowsCapability -Online -Name "Language.OCR~~~zh-TW~0.0.1.0"   # Chinese (Traditional)
Add-WindowsCapability -Online -Name "Language.OCR~~~ko-KR~0.0.1.0"   # Korean
```

The app shows a tray notification if the selected language's OCR pack is missing.

## Translation services

Choose one in the game bar's **Settings** widget (or **Translation settings…** in the tray menu).

| Service | Key | Notes |
| --- | --- | --- |
| **Google Translate** (default) | None | Free, no signup. Uses Google's free web endpoint, which Google may rate-limit under very heavy use. |
| **DeepL** | [Your own](https://www.deepl.com/your-account/keys) | Best quality; the only service that supports the glossary. DeepL's free API plan is enough for most people. Free and Pro keys are both detected. |
| **Google Cloud Translation** | [Your own](https://console.cloud.google.com/apis/credentials) | Google's official API with a free monthly allowance. Needs a Google Cloud project with the Cloud Translation API enabled. |

ScreenTranslator never ships or shares a key. Yours is stored only on your PC, in
`%AppData%\ScreenTranslator\config.json`. The `DEEPL_API_KEY` and `GOOGLE_TRANSLATE_API_KEY` environment
variables override the saved keys.

## Usage

Default hotkeys (change any of them in the game bar › **Settings**):

| Hotkey | Action |
| --- | --- |
| `Ctrl+Shift+G` | Open / close the game bar |
| `Ctrl+Shift+R` | Select a new region (`Esc` keeps the current one) |
| `Ctrl+Shift+P` | Pause / resume (the region frame turns grey while paused) |
| `Ctrl+Shift+O` | Cycle the background opacity (100 → 75 → 50 → 25 → 0 %); the text always stays solid |
| Hold `Ctrl+Shift+H` | Peek: translations hide until you let go |
| `Ctrl+Shift+1` – `4` | Source language: Japanese, Chinese (Simplified), Korean, Chinese (Traditional) |

Everything is also in the tray icon's right-click menu (it shows your current hotkeys), along with
**Text size**, **Translation settings…**, **Open log folder** and **Exit**. Hovering the tray icon shows the
active translation service, and for DeepL, this month's usage.

### Game bar

Press `Ctrl+Shift+G`, double-click the tray icon, or choose **Open game bar** from its menu. The screen
dims and a home bar appears at the top with three widgets:

- **Translator**: status, select region, pause, source and target language, vertical text, background
  opacity, text size, and translation service / DeepL usage.
- **Glossary**: terms that must always translate a certain way (see below).
- **Settings**: translation service and key, how often to check for new text, sentence joining, and the
  hotkey editor. Click a hotkey box and press new keys; Backspace turns a hotkey off and Esc cancels.

Drag widgets by their title bar; positions are remembered. Close the bar with `Esc`, the ✕, or by clicking
empty space, and focus goes back to your game. **Pin** the Translator widget to keep it on screen after the
bar closes. It's then click-through, like pinned widgets in Xbox Game Bar.

The bar is entirely optional: nothing appears unless you summon it, and you can turn its hotkey off.

### Vertical text

Japanese vertical text is recognised automatically. For **vertical Chinese**, or for the most accurate
reading of vertical Japanese, tick **Vertical text** in the Translator widget. ScreenTranslator then finds
each column, cuts it into characters and reads them as horizontal lines, which Windows OCR handles much
better. Columns are read right to left and joined into paragraphs. Select just the text area; drawings
and panel borders inside the region confuse the column detection.

### Glossary

Requires DeepL. Add a row per term, for example `モンキー・D・ルフィ` → `Monkey D. Luffy`, then **Save**
(closing the bar also saves). ScreenTranslator turns the list into a
[DeepL glossary](https://developers.deepl.com/docs/api-reference/glossaries) on your account named
`ScreenTranslator <source>-<target> <id>`, one per language pair. It's reused across sessions and replaced
when you edit the terms. DeepL keeps the term exact *and* builds the grammar around it ("I'm one of
Luffy's crew!"), which a simple find-and-replace can't do.

## Configuration

Settings are saved automatically in `%AppData%\ScreenTranslator\config.json`:

```json
{
  "Service": "GoogleFree",
  "DeepLApiKey": "",
  "GoogleCloudApiKey": "",
  "SourceLanguage": "ja",
  "TargetLanguage": "EN-US",
  "OverlayOpacity": 0.75,
  "TextScale": 1.0,
  "CaptureIntervalMs": 500,
  "MergeLines": true,
  "VerticalText": false,
  "Hotkeys": { "SelectRegion": "Ctrl+Alt+F9", "Peek": "" },
  "Glossary": [ { "Source": "モンキー・D・ルフィ", "Target": "Monkey D. Luffy" } ],
  "GameBarWidgets": {}
}
```

- `Service`: `GoogleFree`, `DeepL` or `GoogleCloud`.
- `TargetLanguage`: a [DeepL-style code](https://developers.deepl.com/docs/resources/supported-languages) (`EN-GB`, `DE`, `ZH-HANT`, …); it's mapped for Google automatically.
- `OverlayOpacity`: opacity of the dark background behind translations only (0–1).
- `TextScale`: multiplies the translation font size (0.5–2.0).
- `CaptureIntervalMs`: how often the region is checked for changes (200–2000).
- `MergeLines`: joins wrapped lines into sentences. Turn it off if a game's menu items get joined together.
- `Hotkeys`: only the ones you changed; `""` turns a hotkey off. Delete the section to restore the defaults.
- `GameBarWidgets`: widget positions and pins; delete it to reset the layout.

### Logs

`%AppData%\ScreenTranslator\logs\screentranslator.log` (tray › **Open log folder**) records startup details,
settings changes, errors and timings, which helps when reporting a bug. It never contains screen text,
translations or API keys. It rolls over at 1 MB.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/GegarinSagolsem/ScreenTranslator.git
cd ScreenTranslator
dotnet run --project ScreenTranslator
```

Every push is built by GitHub Actions ([`build.yml`](.github/workflows/build.yml)). Pushing a version tag
(`git tag v2.0.0 && git push origin v2.0.0`) publishes both download zips to a GitHub release
([`release.yml`](.github/workflows/release.yml)).

## How it works

```
timer ─▶ capture region ─▶ FrameGate: changed and settled? ─no─▶ skip
                                   │ yes
                                   ▼
          Windows OCR (lines, or columns re-laid as lines for vertical text)
                                   ▼
          merge wrapped lines / columns into sentence blocks
                                   ▼
          translate (Google free, DeepL or Google Cloud; batched and cached)
                                   ▼
          labels laid out over each block without overlapping
```

`FrameGate` counts changed pixels (ignoring video-level noise), so even one new character registers. A
changed frame is read once the next frame matches it, or matches the frame from two ticks ago (a
blinking "▼ next" arrow). Scenes that never stop moving are still read every 3 seconds.

| File | Role |
| --- | --- |
| `App.xaml(.cs)` | Startup, single instance, tray menu, notifications, crash logging |
| `MainWindow.xaml(.cs)` | Overlay across all monitors: region selection, capture loop, hotkeys |
| `GameBarWindow.xaml(.cs)` | Optional game bar: widgets, settings, hotkey editor (WPF Fluent theme) |
| `ScreenCapture.cs` | Screen grab, pixel change detection, `FrameGate` settle logic |
| `OcrHelper.cs` | Windows OCR, CJK word joining, merging lines and columns into blocks |
| `VerticalText.cs` | Finds columns of vertical text and re-lays them as horizontal lines for OCR |
| `TranslationHelper.cs` | Translation service choice, batching, caching |
| `GoogleClients.cs`, `DeepLClient.cs` | The three translation services (DeepL also does glossary and usage) |
| `TranslationLayout.cs`, `OutlinedText.cs` | Label placement without overlaps; outlined subtitle-style text |
| `Hotkeys.cs` | Hotkey parsing and the list of bindable commands |
| `AppConfig.cs`, `Languages.cs`, `Log.cs`, `NativeMethods.cs` | Settings, languages, log file, Win32 interop |

## Limitations

- The game bar always opens on the primary monitor.
- Vertical text works best on clean text areas; drawings or panel borders inside the region confuse column detection.
- Very small fonts (under ~10 px) OCR poorly.
- Google's free endpoint is unofficial; under heavy use Google may rate-limit it for a while. Switch to a DeepL or Google Cloud key if that happens.

## Contributing

See [`model.md`](model.md) for how tasks in this repo are split between AI models and agents, and
[`graphify.md`](graphify.md) for the code knowledge graph used to navigate the codebase.

## License

[MIT](LICENSE)
