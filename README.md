# SmoothTube

SmoothTube is a native Windows 11 YouTube client built with **WinUI 3**, **C#**, **WebView2**, and the **YouTube Data API**. It explores a cleaner desktop-first YouTube experience with a focused Windows-style navigation shell, signed-in subscriptions, channel pages, search, local continue-watching support, comments, and official YouTube playback surfaces.

> SmoothTube is an independent client project. It is not affiliated with, endorsed by, sponsored by, or connected to YouTube or Google.

## Current Release

**Latest version:** `v1.1.0`

SmoothTube v1.1.0 focuses on subscriptions, persistent caching, duration badges, livestream/premiere behavior, video page polish, and overall usability.

### v1.1.0 Highlights

- Added persistent JSON-backed caching for Home, Subscriptions, and Channel pages.
- Improved Subscriptions recent uploads ordering with time-based sorting.
- Improved Subscriptions refresh behavior so Recent Uploads can refresh cleanly without wiping cached livestream/premiere results.
- Added duration badge recovery for loaded subscription videos.
- Fixed Continue Watching duration badges using saved duration seconds.
- Improved livestream and premiere loading with quota-aware behavior and cached partial results.
- Improved Video page layout, channel metadata, description handling, and pause behavior.
- Improved thumbnail handling across Search, Video cards, Up next, and Channel content.
- Updated Channel page loaded-count wording to clarify currently loaded content.
- Added a temporary app icon for better visual coherence across Windows, taskbar, Start menu, and app package surfaces.

## Overview

SmoothTube was built as a WinUI 3 desktop app with a modern Windows layout, local settings, WebView2 playback, YouTube Data API integration, and public metadata fallbacks through Invidious/Piped-style endpoints.

The app is designed around a simple idea: keep the browsing experience native and fast, while using official YouTube surfaces for playback and user-controlled Google credentials for account-specific API access.

SmoothTube is still under development and may have bugs, quota limitations, or incomplete features. It is stable enough for experimentation and personal use, but please proceed with patience xD.

## Features

- Native Windows 11 UI built with WinUI 3.
- Left-side navigation for Home, Search, Library, Subscriptions, Channels, and Settings.
- Home page with recommendations and Continue Watching content.
- Search page with video and channel results.
- Video page with embedded YouTube playback, metadata, recommendations, comments, and like/dislike UI state.
- Channel pages with channel artwork, recent uploads, livestream sections, and load-more behavior.
- Signed-in subscriptions view with recent uploads, livestream/premiere grouping, shorts filtering, refresh support, and cached subscription metadata.
- Sidebar channel shortcuts for subscribed channels.
- Local watch history and Continue Watching support.
- Continue Watching progress overlays and resume playback support.
- Google OAuth sign-in using PKCE and a localhost callback.
- YouTube Data API support for search, metadata, comments, subscriptions, channels, and live chat where available.
- Invidious/Piped-style public endpoint fallbacks for non-authenticated home/search metadata when available.
- WebView2 fallback option for videos that cannot be embedded by owner choice.

## Requirements

For normal users:

- Windows 11.
- Microsoft WebView2 Runtime.
- A Google Cloud project with YouTube Data API v3 enabled.
- A YouTube Data API key.
- OAuth Desktop client credentials if you want signed-in features such as subscriptions.

For developers:

- Windows 11.
- Visual Studio 2022 or later.
- .NET 8 SDK.
- Windows App SDK / WinUI 3 workload.
- Microsoft WebView2 Runtime.

---

# Installation

## Update from an older SmoothTube version

You usually do **not** need to uninstall the previous version.

Updating in place is recommended because uninstalling may remove local app data such as:

- API key.
- OAuth Client ID.
- OAuth Client Secret.
- Sign-in/session data, if saved locally.
- Continue Watching history.
- Cached Home, Subscriptions, and Channel data.

To update:

1. Download the latest SmoothTube release ZIP from the GitHub Releases page.
2. Extract the ZIP file.
3. Open the extracted folder.
4. Right-click `Add-AppDevPackage.ps1`.
5. Choose **Run with PowerShell**.
6. Follow the prompts.

Windows should update the existing SmoothTube installation in place as long as the package identity, publisher certificate, and architecture match.

Only uninstall the old version first if Windows shows a package conflict or the update script fails.

## Fresh install

1. Download the latest SmoothTube release ZIP from the GitHub Releases page.
2. Extract the ZIP file.
3. Open the extracted folder.
4. Right-click `Add-AppDevPackage.ps1`.
5. Choose **Run with PowerShell**.
6. Follow the prompts shown by PowerShell.
7. Launch SmoothTube from the Start menu.
8. Open SmoothTube Settings and add your YouTube API credentials.

## If PowerShell closes immediately

If double-clicking or right-clicking `Add-AppDevPackage.ps1` closes PowerShell immediately, run it manually instead:

1. Open the extracted SmoothTube release folder.
2. Click the address bar in File Explorer.
3. Type `powershell`.
4. Press Enter.
5. Run:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Add-AppDevPackage.ps1
```

This same script is used for both fresh installs and updates. If you already have an older SmoothTube version installed, Windows should update it automatically.

## If Windows reports a package conflict

If Windows reports that the package conflicts with an existing install:

1. Open Windows Settings.
2. Go to **Apps → Installed apps**.
3. Search for `SmoothTube`.
4. Uninstall the old SmoothTube package.
5. Run `Add-AppDevPackage.ps1` again.

Only do this if the normal update fails, because uninstalling can remove local app data.

---

# YouTube API Setup

SmoothTube requires your own YouTube Data API credentials for subscription features, authenticated YouTube actions, and API-backed metadata.

You will need:

- YouTube Data API v3 API key.
- OAuth Client ID.
- OAuth Client Secret.

## Step 1: Create a Google Cloud project

1. Go to Google Cloud Console.
2. Sign in with your Google account.
3. Create a new project.
4. Give it a name such as `SmoothTube`.
5. Select the project after creating it.

## Step 2: Enable YouTube Data API v3

1. In Google Cloud Console, open **APIs & Services**.
2. Go to **Library**.
3. Search for `YouTube Data API v3`.
4. Open it.
5. Click **Enable**.

## Step 3: Create an API key

1. Go to **APIs & Services → Credentials**.
2. Click **Create Credentials**.
3. Choose **API key**.
4. Copy the generated API key.
5. Optional but recommended: restrict the key to **YouTube Data API v3**.

The API key is used for public metadata features such as search, video metadata, comments, channel data, and other non-private YouTube Data API requests.

## Step 4: Configure OAuth consent

1. Go to **APIs & Services → OAuth consent screen**.
2. Choose the user type available to you.
3. Fill in the required app information.
4. Add your own Google account as a test user if the app is in testing mode.
5. Save the consent screen.

If the OAuth app is still in testing mode, only added test users may be able to sign in.

## Step 5: Create OAuth Client ID and Secret

1. Go to **APIs & Services → Credentials**.
2. Click **Create Credentials**.
3. Choose **OAuth client ID**.
4. For application type, choose **Desktop app**.
5. Name it something like `SmoothTube Desktop`.
6. Click **Create**.
7. Copy the **Client ID** and **Client Secret**.

OAuth is used for account-related features such as subscriptions and signed-in YouTube API access.

## Step 6: Add credentials in SmoothTube

1. Open SmoothTube.
2. Go to **Settings**.
3. Paste your:
   - YouTube API Key.
   - OAuth Client ID.
   - OAuth Client Secret.
4. Save the settings.
5. Sign in with Google from SmoothTube.
6. Approve the requested YouTube permissions.

After this, SmoothTube should be able to load your subscriptions, recent uploads, livestreams, premieres, and authenticated YouTube actions.

---

# Running from Source

1. Clone the repository.

```bash
git clone https://github.com/tatsumioga1/SmoothTube.git
cd SmoothTube
```

2. Open the solution:

```text
SmoothTube/SmoothTube.slnx
```

3. In Visual Studio, set the package project as the startup project.
4. Restore NuGet packages if Visual Studio does not restore them automatically.
5. Build the solution.
6. Run the app.
7. Open Settings inside the app.
8. Add your own YouTube Data API key.
9. Optional: add your OAuth Desktop client ID and secret, then sign in to enable subscriptions and signed-in API features.

## Visual Studio Setup

Install the following workloads/components in Visual Studio:

- **.NET desktop development**.
- **Windows application development** / Windows App SDK tooling.
- **WinUI application development tools**, if shown as an individual component.
- .NET 8 SDK.
- Windows App SDK runtime/tooling.
- WebView2 Runtime.

If the solution does not launch immediately, make sure the packaged app project is selected as the startup project.

---

# Project Structure

```text
SmoothTube-WinUI-/
|-- README.md
|-- LICENSE
|-- .gitignore
`-- SmoothTube/
    |-- SmoothTube.slnx
    `-- SmoothTube/
        |-- SmoothTube/
        |   |-- Assets/
        |   |   `-- youtube-player.html
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
- **Service layer**: `IYouTubeService` and `YouTubeService` centralize YouTube Data API calls, Invidious/Piped-style fallback queries, metadata enrichment, fallback parsing, subscription loading, comments, live chat, and channel data.
- **Settings layer**: `AppSettings` stores API keys, OAuth client details, and tokens in local Windows app settings.
- **OAuth layer**: `GoogleOAuthService` implements Google sign-in using PKCE and a localhost redirect.
- **Playback layer**: videos play through WebView2 using official YouTube embed/player surfaces.
- **Watch history layer**: `WatchHistoryService` tracks local continue-watching progress, resume position, duration, and last watched state.
- **Persistent cache layer**: local JSON-backed caching improves startup and page reload speed for Home, Subscriptions, and Channel pages.

The app intentionally avoids downloading or bypassing YouTube playback restrictions. Videos that cannot be embedded can be opened through YouTube instead.

---

# Credentials and Security

This repository does **not** include personal Google credentials.

Each user/developer must provide their own:

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

Do not commit your API keys, OAuth secrets, access tokens, or refresh tokens.

---

# YouTube and API Notes

SmoothTube uses a combination of:

- YouTube Data API for official metadata and authenticated data.
- OAuth for user-authorized subscription and account-related access.
- Invidious/Piped-style public endpoints for limited unauthenticated home/search fallback metadata.
- WebView2 for official YouTube playback surfaces.
- Local caching to reduce repeated subscription loading.

Some YouTube data is dependent on API availability, quota, video owner settings, public endpoint availability, and what YouTube exposes through official endpoints.

Public fallback endpoints can change, go offline, rate-limit requests, or return incomplete metadata. Videos with embedding disabled are handled through a YouTube watch option rather than bypassing restrictions.

## API quota notes

The YouTube Data API has quota limits. Subscription, livestream, premiere, search, comments, and metadata requests may be limited by your API project's daily quota.

SmoothTube includes caching and fallback behavior, but it cannot bypass Google API quota limits.

If subscription livestream or premiere scanning stops, it may be because the YouTube Search API quota has been exhausted for the day.

## Ads and playback notes

The SmoothTube app itself does not block or remove YouTube ads.

Playback happens through embedded YouTube player surfaces, so playback behavior, ads, restrictions, and availability are governed by YouTube.

---

# Continue Watching and Resume Playback

SmoothTube stores continue-watching progress locally. The app tracks:

- Video ID.
- Title and channel metadata.
- Thumbnail and duration.
- Resume timestamp.
- Duration in seconds.
- Progress percentage.
- Last watched time.

Progress is displayed as a thumbnail overlay on supported video cards. When a video is reopened, SmoothTube attempts to resume from the saved watch position.

The watch history data is local runtime app data and is not committed to the repository.

---

# Troubleshooting

## The app does not install

Try running PowerShell manually from the extracted release folder:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Add-AppDevPackage.ps1
```

If Windows reports a package conflict, uninstall the previous SmoothTube package and then install again.

## PowerShell opens and closes immediately

Run the script manually:

1. Open the extracted release folder.
2. Click the File Explorer address bar.
3. Type `powershell`.
4. Press Enter.
5. Run:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Add-AppDevPackage.ps1
```

## Subscriptions do not load

Check that:

- You are signed in from SmoothTube Settings.
- YouTube Data API v3 is enabled in Google Cloud Console.
- Your API key is correct.
- Your OAuth Client ID and Client Secret are correct.
- Your Google account is added as a test user if your OAuth app is still in testing mode.
- The Google account has YouTube subscriptions.
- Your API quota has not been exhausted.

## Livestreams or premieres are missing

Livestream and premiere detection uses YouTube Search API quota.

If the quota is exhausted, SmoothTube may show cached or partial live/premiere results until the quota resets.

## Duration badges are missing

SmoothTube tries to recover duration badges through API metadata and lightweight fallback checks.

Some videos may temporarily show without duration if YouTube metadata is unavailable or quota is limited.

## Some videos do not play inside the app

Some videos have embedding disabled by the owner. SmoothTube does not bypass that restriction. Use the **Open on YouTube** option for those videos.

## Continue Watching does not update immediately

- Watch a video for at least a few seconds.
- Navigate back using the app back button.
- Restart the app if local state appears stale.
- Make sure the video is not a livestream or premiere, because those are treated differently.

## Player controls feel laggy while debugging

This can happen when running under the Visual Studio debugger because of debug output, XAML diagnostics, and WebView2 debugging overhead.

Test outside the debugger before assuming it is a runtime performance issue.

---

# Current Limitations

- Playback uses YouTube's official embedded/player surfaces, so some controls and behaviors are governed by YouTube.
- Some videos cannot be embedded because the owner disables playback on other websites.
- Live chat and comment functionality is read-oriented where available.
- API quota and Google account permissions affect which features are available.
- Invidious/Piped-style fallback data is best-effort and may vary by public instance availability.
- Downloads are not implemented because SmoothTube should not bypass YouTube restrictions or unsupported offline flows.
- Livestream and premiere detection can be limited by API quota and public metadata availability.
- Running under the Visual Studio debugger can make WebView2/player UI feel heavier than normal execution.

---

# Roadmap

- Improve subscription freshness and livestream/premiere detection.
- Refine fullscreen playback behavior.
- Improve channel pages with playlists and richer channel metadata.
- Expand Library with better watch history and saved video workflows.
- Add more robust visual loading states and offline/error handling.
- Continue improving packaging and setup experience.

---

# Contributing

Feedback, bug reports, and suggestions are welcome.

This project is source-available for viewing and reference, but it is not open source. Please do not copy, modify, redistribute, publish, rebrand, or reuse the code, assets, design, or project structure without prior written permission.

If you want to contribute or collaborate, please contact the project owner first.

---

# AI Assistance Disclosure

Parts of SmoothTube were developed with AI-assisted coding support for debugging, refactoring, documentation, and implementation guidance.

The project direction, feature decisions, testing, integration, and final code review were handled by the project maintainer.

AI assistance does not change the project license, ownership, or usage restrictions.

---

# License

SmoothTube is released as **source-available / all rights reserved**.

You may view the repository for learning and reference. You may not copy, modify, redistribute, publish, rebrand, sublicense, or use the project commercially without prior written permission from the copyright holder.

See [`LICENSE`](LICENSE) for the full license terms.
