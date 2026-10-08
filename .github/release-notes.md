## Which download?

| File | For |
| --- | --- |
| **`ScreenTranslator-{version}-win-x64.zip`** | Most people (Intel / AMD PCs). One `.exe`, nothing else to install. |
| `ScreenTranslator-{version}-win-arm64.zip` | Windows 11 on ARM laptops (e.g. Snapdragon). Native ARM64, not yet tested on real hardware. |
| `ScreenTranslator-{version}-win-x64-needs-dotnet10.zip` | Smaller, needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |

Unzip and run `ScreenTranslator.exe`. Translation works right away with free Google Translate; DeepL and Google
Cloud are optional with your own key. The files aren't code-signed, so Windows SmartScreen may ask you to
confirm: **More info › Run anyway**.

## System requirements

- **OS:** Windows 11, or Windows 10 version 2004 (May 2020 Update) or newer, 64-bit. Not S mode.
- **Memory:** 4 GB RAM or more (the app uses about 200–350 MB).
- **Storage:** about 90 MB (standalone) or about 30 MB plus the .NET 10 Desktop Runtime.
- **Internet** for translation; text recognition runs offline.
- **OCR pack:** the Windows OCR pack for each language you read ([how to install](https://github.com/GegarinSagolsem/ScreenTranslator#installing-ocr-languages)).
- Not for macOS, Linux, mobile, 32-bit Windows, or Windows 7 / 8 / 8.1.
- Games: use windowed or borderless (fullscreen windowed) mode.

