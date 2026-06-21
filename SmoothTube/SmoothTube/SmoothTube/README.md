# SmoothTube

SmoothTube is a native Windows 11 YouTube client built with WinUI 3.

## Current status

- Native WinUI shell with Mica, left navigation, and separate Home, Search, Video, Library, Downloads, and Settings pages.
- Shared in-app video catalog used by Home, Search, and Library.
- Search results and library videos navigate into the video detail page.
- `IYouTubeService` abstraction with local fallback data.
- YouTube Data API search support when an API key is saved in Settings.
- Search enrichment for YouTube durations, view counts, and publish dates.
- Livestream detection through YouTube video metadata.
- Read-only video comments and read-only live chat tabs on the video page.
- WebView2 playback for real YouTube search results.
- Google OAuth sign-in scaffold using PKCE and localhost callback.
- Signed-in subscriptions page backed by the YouTube Data API, including paged subscription loading.
- Channel pages with recent uploads from each channel's uploads playlist.
- Channel banners when YouTube exposes them through channel branding.
- Signed-in home row backed by recent uploads from subscribed channels.
- Responsive video page layout with recommended videos.

## Roadmap

1. Add richer channel pages with playlists, community-safe metadata, and channel statistics.
2. Add posting comments, sending live chat, likes, and playlist actions through OAuth scopes.
3. Add more home feed sections backed by authenticated subscriptions, activities, playlists, and watch-context-friendly API data.
4. Improve playback presentation while staying on official YouTube playback surfaces.
5. Store watch history, progress, saved videos, and settings locally.
6. Build downloads only for user-owned/local media or supported offline flows. Avoid bypassing YouTube restrictions.
7. Package and sign the app for local install.

## Notes

The app uses local sample data only when no YouTube API key is configured. Once an API key is saved, Home, Search, and recommendations use YouTube data instead of the sample catalog.

Google sign-in requires an OAuth Desktop client ID and secret from Google Cloud Console. Configure the same Google Cloud project for the YouTube Data API, then paste the desktop client details in Settings.

API keys, OAuth client details, access tokens, and refresh tokens are read from the app's local Windows settings at runtime. Do not commit personal credential files or local settings exports; each developer should configure their own credentials locally after cloning.

Downloads are intentionally not exposed in the app shell. SmoothTube should avoid downloading YouTube videos unless a future official/offline-capable API path permits it.
