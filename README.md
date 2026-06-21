# SmoothTube

SmoothTube is a native Windows 11 YouTube client built with WinUI 3. It explores a cleaner desktop-first YouTube experience with a focused navigation shell, signed-in subscriptions, channel pages, search, watch history, comments, and official YouTube playback surfaces.

> SmoothTube is an independent client project. It is not affiliated with, endorsed by, or sponsored by YouTube or Google.

## Overview

SmoothTube was built as a WinUI 3 desktop app with a modern Windows layout, local settings, WebView2 playback, and YouTube Data API integration. The app is designed around a simple idea: keep the browsing experience native and fast, while using official YouTube surfaces for playback and user-controlled Google credentials for API access.

## Features

- Native Windows 11 UI built with WinUI 3.
- Left-side navigation for Home, Search, Library, Subscriptions, Channels, and Settings.
- Home page with recommendations and continue-watching content.
- Search page with mixed video and channel results.
- Video page with embedded YouTube playback, metadata, recommendations, comments, and like/dislike UI state.
- Channel pages with channel artwork, recent uploads, livestream sections, and load-more behavior.
- Signed-in subscriptions view with recent uploads, livestream/premiere grouping, shorts filtering, refresh support, and cached subscription metadata.
- Sidebar channel shortcuts for subscribed channels.
- Local watch history and continue-watching support.
- Google OAuth sign-in using PKCE and a localhost callback.
- YouTube Data API support for search, metadata, comments, subscriptions, channels, and live chat where available.
- WebView2 fallback option for videos that cannot be embedded by owner choice.

## Project Structure

```text
SmoothTube-WinUI-/
|-- README.md
|-- .gitignore
`-- SmoothTube/
    |-- SmoothTube.slnx
    `-- SmoothTube/
        |-- SmoothTube/
        |   |-- Assets/
        |   |-- controls/
        |   |-- Models/
        |   |-- Services/
        |   |-- App.xaml
        |   |-- MainWindow.xaml
        |   |-- HomePage.xaml
        |   |-- SearchPage.xaml
        |   |-- SubscriptionsPage.xaml
        |   |-- ChannelPage.xaml
        |   |-- VideoPage.xaml
        |   `-- SmoothTube.csproj
        `-- SmoothTube (Package)/
            |-- Images/
            |-- Package.appxmanifest
            `-- SmoothTube (Package).wapproj
```

## How It Was Built

SmoothTube is built around a small set of layers:

- **WinUI 3 shell**: `MainWindow.xaml` hosts the desktop navigation and page frame.
- **Pages**: Home, Search, Library, Subscriptions, Channel, Video, and Settings are separate XAML pages.
- **Reusable cards**: video cards are implemented as shared controls so thumbnails, duration badges, live/premiere tags, and progress state are consistent across the app.
- **Service layer**: `IYouTubeService` and `YouTubeService` centralize YouTube Data API calls, metadata enrichment, fallback parsing, subscription loading, comments, live chat, and channel data.
- **Settings layer**: `AppSettings` stores API keys, OAuth client details, and tokens in local Windows app settings.
- **OAuth layer**: `GoogleOAuthService` implements Google sign-in using PKCE and a localhost redirect.
- **Playback layer**: videos play through WebView2 using official YouTube embed/player surfaces.
- **Watch history layer**: `WatchHistoryService` tracks local continue-watching progress.

The app intentionally avoids downloading or bypassing YouTube playback restrictions. Videos that cannot be embedded can be opened through YouTube instead.

## Requirements

- Windows 11.
- Visual Studio 2022 or later.
- .NET 8 SDK.
- Windows App SDK / WinUI 3 workload.
- Microsoft WebView2 Runtime.
- A Google Cloud project with the YouTube Data API enabled, if you want live YouTube data.

## Getting Started

1. Clone the repository.
2. Open the solution:

```text
SmoothTube/SmoothTube.slnx
```

3. In Visual Studio, select the package project or app startup profile.
4. Build and run.
5. Open Settings inside the app.
6. Add your own YouTube Data API key.
7. Optional: add your OAuth Desktop client ID and secret, then sign in to enable subscriptions and signed-in API features.

## Credentials and Security

This repository does **not** include personal Google credentials.

Each developer must provide their own:

- YouTube Data API key.
- OAuth Desktop client ID.
- OAuth Desktop client secret.

These values are entered in the app Settings page and stored locally by Windows app settings at runtime. Access tokens and refresh tokens are also local runtime data.

The following are intentionally ignored by `.gitignore`:

- Visual Studio workspace cache.
- Build output such as `bin/` and `obj/`.
- Packaged app output.
- Local user files such as `*.user`.
- Local credential-style files such as `.env`, `client_secret*.json`, `token*.json`, and `credentials*.json`.

Before publishing, the source tree was checked for common Google API key, OAuth client ID, client secret, access token, and refresh token patterns.

## YouTube and API Notes

SmoothTube uses a combination of:

- YouTube Data API for official metadata and authenticated data.
- OAuth for user-authorized subscription and account-related access.
- WebView2 for official YouTube playback surfaces.
- Local caching to reduce repeated subscription loading.

Some YouTube data is dependent on API availability, quota, video owner settings, and what YouTube exposes through official endpoints. Videos with embedding disabled are handled through a YouTube watch option rather than bypassing restrictions.

## Current Limitations

- Playback uses YouTube's official embedded/player surfaces, so some controls and behaviors are governed by YouTube.
- Some videos cannot be embedded because the owner disables playback on other websites.
- Live chat and comment functionality is read-oriented where available.
- API quota and Google account permissions affect which features are available.
- Downloads are not implemented because SmoothTube should not bypass YouTube restrictions or unsupported offline flows.

## Roadmap

- Improve subscription freshness and livestream/premiere detection.
- Refine fullscreen playback behavior.
- Improve channel pages with playlists and richer channel metadata.
- Expand Library with better watch history and saved video workflows.
- Add more robust visual loading states and offline/error handling.
- Package and sign the app for local installation.

## Contributing

Contributions are welcome. Please avoid committing local credentials, generated build output, packaged binaries, or machine-specific Visual Studio files.

## License

See the project license file for details.
