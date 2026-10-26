# Components and notices

The Git repository contains first-party application sources and a parser-derived option catalog. The catalog descriptions come from yt-dlp 2026.10.21 under the Unlicense; its license is retained in `licenses/yt-dlp/LICENSE`.

The Windows release includes the Microsoft WebView2 SDK redistributables. Their license and notices are included in the release `licenses/webview2` directory. WebView2 Runtime is supplied separately by Microsoft.

Media components are fetched directly from their official upstream distributions by the user's local setup script, rather than redistributed in this Git history or GUI release ZIP:

| Component | Official distribution/source | Notice |
| --- | --- | --- |
| yt-dlp 2026.10.21 | https://github.com/yt-dlp/yt-dlp/releases/tag/2026.10.21 | Unlicense with bundled third-party components; upstream `THIRD_PARTY_LICENSES.txt` |
| Deno 2.9.8 | https://github.com/denoland/deno/releases/tag/v2.9.8 | MIT and upstream third-party notices |
| FFmpeg / FFprobe | https://github.com/yt-dlp/FFmpeg-Builds/releases/latest | GPL shared build, version-dependent; license comes from the official archive |
| WebView2 SDK 1.0.4314.35 | https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35 | Microsoft redistribution license and notices |

The setup script verifies SHA256 values for yt-dlp and Deno against the pinned versions. It obtains the current FFmpeg shared asset's SHA256 from the official GitHub release metadata, and records the exact URL and hash in the local component manifest.

No API key, payment service or cloud processing is required by Flow. GitHub's public download endpoints can impose availability and rate limits.
