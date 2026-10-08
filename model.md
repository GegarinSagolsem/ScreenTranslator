# Model routing

Which Claude model, effort level and agent to use for each kind of task in this repo.
Pick the cheapest row that fits; move up a tier only when the task touches something in the
**Opus** rows or a cheaper run already failed.

## Ground rules

- **Never use Fable 5.** Route only to the three models below.
- **Graph first.** Read `graphify-out/GRAPH_REPORT.md` and use `graphify query/explain/path` before opening files (see [`graphify.md`](graphify.md)). Refresh the graph after code changes.
- **No secrets in source.** The DeepL key lives in `%AppData%\ScreenTranslator\config.json` or `DEEPL_API_KEY`. Any task that touches key handling also runs `/security-review`.
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
| Bug or feature inside one helper (`TranslationHelper`, `AppConfig`, `Languages`, `ApiKeyWindow`) | Sonnet 5 | medium | inline |
| NuGet or target-framework upgrades | Sonnet 5 | medium | inline, then `/run` |
| Unit tests for pure logic (`JoinWords`, `HasChanged`, DeepL client with a fake `HttpMessageHandler`) | Sonnet 5 | medium | **general-purpose** agent |
| Reviewing a normal diff | Sonnet 5 | medium | `/code-review medium` |
| Capture → OCR → translate pipeline (`ProcessFrameAsync`, timer re-entrancy, frame versioning) | Opus 5.5 | high | inline |
| Win32 interop (`NativeMethods`, hotkeys, window styles, capture exclusion, DPI) | Opus 5.5 | high | inline |
| Cross-file refactor of `MainWindow` (the graph's god node) | Opus 5.5 | high | **Plan** agent first, then inline |
| New architecture-level feature | Opus 5.5 | xhigh | **Plan** agent, then implement |
| API key storage, network code, anything before a public push | Opus 5.5 | high | `/security-review` |
| Reviewing a pipeline or interop diff | Opus 5.5 | high | `/code-review high` |

## Backlog

Open improvements, each pre-routed.

| # | Task | Model | Effort | Agent / skill |
| --- | --- | --- | --- | --- |
| 1 | Multi-monitor support (virtual screen bounds, per-monitor DPI) | Opus 5.5 | xhigh | Plan → inline → `/run` |
| 2 | Upscale small regions before OCR to read tiny fonts | Sonnet 5 | medium | inline, test with the scratch harness approach |
| 3 | Unit test project (`JoinWords`, `HasChanged`, `TranslationHelper`, `AppConfig`) | Sonnet 5 | medium | general-purpose |
| 4 | GitHub Actions workflow: build on push, attach publish zip to releases | Haiku 4.5 | low | inline |
| 5 | Target-language submenu in the tray (writes `TargetLanguage`) | Sonnet 5 | medium | inline |
| 6 | Configurable hotkeys in `config.json` | Sonnet 5 | medium | inline |
| 7 | Encrypt the saved API key with DPAPI (`ProtectedData`) | Sonnet 5 | medium | inline → `/security-review` (Opus 5.5) |
| 8 | Vertical Japanese text layout | Opus 5.5 | high | Plan → inline |
| 9 | Settings window (interval, opacity, languages, key) | Sonnet 5 | medium | inline |

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
