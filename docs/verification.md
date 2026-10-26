# Flow 1.1 verification

Verified locally on Windows on 2026-10-26.

- 11 frontend checks: complete parser catalog, form arity, search, escaping, sensitive preset filtering, time formats, invalid bounds, resizing, translation, native range arguments.
- 343 core checks: version parity, parser option mappings, argument quoting, ordered repeated options, approval boundaries, actual HTTP media extraction/download, precise sections with distinct names, MP3 conversion, query output, stdin, binary stdout, cancellation, retry, privacy filtering, settings and restart recovery.
- 19 actual WebView2 UI checks: collapsed initial screen, source duration, inward/outward trim, moving selection, zoom/fit, keyboard precision, cursor boundary, invalid interval rejection, field synchronization, preset restoration, source invalidation, normal download-button submission, produced media duration, disabling range, no uncaught UI errors.

The UI case generated a 12-second local video, selected 1.250–3.250 seconds, downloaded through the production bridge and yt-dlp/FFmpeg, and measured a 2.000-second output. Two 1-second cuts of a 15fps fixture measured 1.067 seconds, within the declared 0.15-second tolerance.

These are finite synthetic media fixtures and integration checks. They do not verify every supported website, authentication flow, live source, plugin, or combination of yt-dlp options. Updating an engine beyond the catalog version is displayed as unverified compatibility.

Repository publication checks additionally cover a standalone source build, official component setup with SHA256 verification, package contents, and exclusion of local data and credentials.
