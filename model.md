# Model routing

Which Claude model, effort level and agent to use for each kind of task in this repo.
Pick the cheapest row that fits; move up a tier only when the task touches something in the
**Opus** rows or a cheaper run already failed.

## Ground rules

- **Never use Fable 5.** Route only to the three models below.
- **Graph first.** Read `graphify-out/GRAPH_REPORT.md` and use `graphify query/explain/path` before opening files (see [`graphify.md`](graphify.md)). Refresh the graph after code changes.
- **No secrets in source.** Users' own DeepL / Google Cloud keys live in `%AppData%\ScreenTranslator\config.json` or the `DEEPL_API_KEY` / `GOOGLE_TRANSLATE_API_KEY` variables; the app never ships a key, and the log never records one. Any task that touches key handling also runs `/security-review`.
- **Commits:** one short, clean sentence. No co-author lines, no AI or model names.
- **Done means built.** Every code task ends with `dotnet build` passing with 0 warnings.

## Models

| Model | ID | Strengths here |
| --- | --- | --- |
| **Opus 5.5** | `claude-opus-5-5` | Threading, async re-entrancy, Win32 interop, DPI maths, architecture, security |
| **Sonnet 5** | `claude-sonnet-5` | Day-to-day features and fixes contained in one or two files, XAML, reviews |
| **Haiku 4.5** | `claude-haiku-4-5-20251001` | Search, docs, config files, builds, commit messages, mechanical edits |

Switch the session model with `/model opus`, `/model sonnet` or `/model haiku`. When delegating,
pass the same alias as the Agent tool's `model`, or set `model:` in a `.claude/agents/*.md`
frontmatter.

## Effort levels

| Effort | Use when |
| --- | --- |
| `low` | The answer is mechanical or already known: rename, doc edit, run a build |
| `medium` | A normal fix or feature with a clear spec, one or two files |
| `high` | Concurrency, interop, cross-file refactors, anything that can fail only at runtime |
| `xhigh` | Design choices with real trade-offs (multi-monitor, new OCR backend) |

## Task routing

| Task | Model | Effort | Agent / skill |
| --- | --- | --- | --- |
| Find where something lives / what calls what | Haiku 4.5 | low | `graphify query`, then **Explore** agent if needed |
| README, `model.md`, comments, `.gitignore` | Haiku 4.5 | low | inline |
| Build, publish, smoke-launch the app | Haiku 4.5 | low | `/run` |
| Write commit messages, tag a release | Haiku 4.5 | low | inline |
| XAML / overlay styling / tray menu changes | Sonnet 5 | medium | inline |
| Overlay label layout (`TranslationLayout`, `OutlinedText`) | Sonnet 5 | medium | inline, verify with an offscreen render + overlap count |
| Game bar widgets and layout (`GameBarWindow`, Fluent theme styles) | Sonnet 5 | medium | inline, verify by rendering offscreen (the bar is hidden from screenshots) |
| Bug or feature inside one helper (`AppConfig`, `Languages`, `Hotkeys`, `Log`) | Sonnet 5 | medium | inline |
| A translation service (`GoogleClients`, `DeepLClient`) | Sonnet 5 | medium | inline, test against the live API |
| Vertical text / OCR layout (`VerticalText`, `OcrHelper.MergeLines`) | Opus 5.5 | high | inline, test with rendered pages |
| CI / release workflows (`.github/workflows`) | Haiku 4.5 | low | inline, run the publish steps locally first |
| NuGet or target-framework upgrades | Sonnet 5 | medium | inline, then `/run` |
| Unit tests for pure logic (`JoinWords`, `HasChanged`, DeepL client with a fake `HttpMessageHandler`) | Sonnet 5 | medium | **general-purpose** agent |
| Reviewing a normal diff | Sonnet 5 | medium | `/code-review medium` |
| Capture → OCR → translate pipeline (`ProcessFrameAsync`, `FrameGate`, line merging, frame versioning) | Opus 5.5 | high | inline, test with synthetic frame sequences |
| Win32 interop (`NativeMethods`, hotkeys, window styles, capture exclusion, multi-monitor DPI) | Opus 5.5 | high | inline, end-to-end test with a simulated drag |
| Cross-file refactor of `MainWindow` (the graph's god node) | Opus 5.5 | high | **Plan** agent first, then inline |
| New architecture-level feature | Opus 5.5 | xhigh | **Plan** agent, then implement |
| DeepL glossary sync (resources created/deleted on the user's account) | Opus 5.5 | high | inline, test against the live API and clean up after |
| API key storage, network code, anything before a public push | Opus 5.5 | high | `/security-review` |
| Reviewing a pipeline or interop diff | Opus 5.5 | high | `/code-review high` |

## Backlog

Open improvements, each pre-routed. Numbers stay fixed when items are done, so they can be referred to by number.

| # | Task | Model | Effort | Agent / skill |
| --- | --- | --- | --- | --- |
| 5 | Subtitle mode: all translations in one panel below the region | Sonnet 5 | medium | inline |
| 6 | Translation history widget in the game bar, copy last line | Sonnet 5 | medium | inline |
| 7 | One-shot snip-and-translate mode | Sonnet 5 | medium | inline |
| 10 | Upscale small regions before OCR to read tiny fonts | Sonnet 5 | medium | inline, test with the scratch harness approach |
| 12 | Encrypt the saved API keys with DPAPI (`ProtectedData`) | Sonnet 5 | medium | inline → `/security-review` (Opus 5.5) |
| 14 | Start with Windows toggle | Haiku 4.5 | low | inline |
| 16 | Unit test project (`Hotkey.Parse`, `FrameGate`, `MergeLines`, `VerticalText`, translation clients with a fake `HttpMessageHandler`) | Sonnet 5 | medium | general-purpose |
| 18 | Vertical text on full manga pages: ignore drawings and panel borders, detect speech bubbles | Opus 5.5 | xhigh | Plan → inline, test on real pages |
| 19 | Open the game bar on the monitor under the mouse | Sonnet 5 | medium | inline |

Dropped by the owner: #3 (remember last region), #8 (Claude translator; free options preferred).

## Done

Completed in the 2026-10-08 overhaul session.

| Task | Model |
| --- | --- |
| Move DeepL key out of source into `config.json` + first-run key dialog | Opus 5.5 |
| Fix bitmap leak, overlapping ticks, crash-on-exception, pause/reselect state bugs | Opus 5.5 |
| Batched + cached DeepL requests, Free/Pro endpoint detection, user-facing errors | Opus 5.5 |
| Join CJK OCR words without spaces; match `zh-Hans` to installed `zh-Hans-CN` | Opus 5.5 |
| Extract `NativeMethods`, `Languages`; single instance; tray language menu | Opus 5.5 |
| Retarget to .NET 10 LTS, drop redundant packages | Opus 5.5 |
| README, `.gitignore`, `model.md` | Opus 5.5 |
| DeepL usage in the tray tooltip and game bar | Opus 5.5 |
| Glossary backed by DeepL glossaries (chosen after comparing four approaches on the live API) | Opus 5.5 |
| Optional Xbox Game Bar–style panel: Translator / Glossary / Settings widgets, drag, pin, target-language picker | Opus 5.5 |
| Backlog #1: `FrameGate` waits for text to settle (pixel-level change detection, blink-aware, 3 s cap) | Opus 5.5 |
| Backlog #2: merge wrapped OCR lines into sentence blocks (toggle in Settings) | Opus 5.5 |
| Backlog #4: hold `Ctrl+Shift+H` to peek at the original text | Opus 5.5 |
| Fix overlapping labels (`TranslationLayout` fit-to-room), merging on web-style line spacing and text wrapping around photos | Opus 5.5 |
| Background-only opacity down to 0 % with outlined subtitle-style text (`OutlinedText`) | Opus 5.5 |
| Adjustable text size (game bar slider, tray Larger/Smaller/Reset, live re-layout) | Opus 5.5 |
| #15: rolling log file (no screen text, translations or keys) + crash logging | Opus 5.5 |
| Free Google Translate as default; DeepL and Google Cloud with the user's own key | Opus 5.5 |
| #11: configurable hotkeys with an in-app editor | Opus 5.5 |
| #9: overlay across all monitors, per-monitor DPI | Opus 5.5 |
| #13: vertical Japanese and Chinese (column re-layout for OCR), Traditional Chinese | Opus 5.5 |
| #17: GitHub Actions build + release (standalone single-file exe) | Opus 5.5 |
| MIT license, README rewrite | Opus 5.5 |
