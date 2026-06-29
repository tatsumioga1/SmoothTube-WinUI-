using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SmoothTube.Models;
using SmoothTube.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmoothTube
{
    public sealed partial class ChannelPage : Page
    {
        private static readonly HttpClient ChannelMetadataHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private const string ChannelCacheFileName = "channel-page-cache-v3.json";

        private sealed class ChannelPageCacheEntry
        {
            public ChannelItem Channel { get; set; } = new();
            public List<VideoItem> Videos { get; set; } = [];
            public int RequestedUploadCount { get; set; }
            public int RequestedShortCount { get; set; }
            public int RequestedLivestreamCount { get; set; }
            public bool ShortsLoaded { get; set; }
            public bool LivestreamsLoaded { get; set; }
            public bool? IsSubscribed { get; set; }
            public DateTimeOffset LastLoadedAt { get; set; } = DateTimeOffset.Now;
        }

        private static readonly Dictionary<string, ChannelPageCacheEntry> ChannelCache =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool triedPersistentChannelCache;

        public ChannelItem Channel { get; set; } = new();

        public ObservableCollection<VideoItem> UploadVideos { get; } = [];

        public ObservableCollection<VideoItem> ShortVideos { get; } = [];

        public ObservableCollection<VideoItem> LivestreamVideos { get; } = [];

        private List<VideoItem> allVideos = [];

        public string StatusText { get; set; } = "";

        public string ChannelSubscriberText { get; set; } = "";

        public Visibility ChannelSubscriberVisibility { get; set; } = Visibility.Collapsed;

        public string LoadingText { get; set; } = "Loading channel...";

        public Visibility LoadingVisibility { get; set; } = Visibility.Collapsed;

        public Visibility RefreshIconVisibility { get; set; } = Visibility.Visible;

        public Visibility RefreshProgressVisibility { get; set; } = Visibility.Collapsed;

        public bool IsRefreshing { get; set; }

        private int requestedUploadCount = 24;
        private int requestedShortCount = 24;
        private int requestedLivestreamCount = 24;
        private bool shortsLoaded;
        private bool livestreamsLoaded;
        private bool isLoadingMore;
        private bool isLoadingTab;

        protected override void OnNavigatedTo(
            NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is ChannelItem channel)
            {
                Channel = channel;
                requestedUploadCount = 24;
                requestedShortCount = 24;
                requestedLivestreamCount = 24;
                shortsLoaded = false;
                livestreamsLoaded = false;
                ChannelSubscriberText = "";
                ChannelSubscriberVisibility = Visibility.Collapsed;
                allVideos.Clear();
                UploadVideos.Clear();
                ShortVideos.Clear();
                LivestreamVideos.Clear();
                SetLoading("Loading channel uploads...");
                Bindings.Update();
                UpdateBannerVisibility();
                _ = LoadChannelAsync(false);
            }
        }

        public ChannelPage()
        {
            InitializeComponent();

            Loaded += ChannelPage_Loaded;
            SizeChanged += ChannelPage_SizeChanged;
        }

        private void ChannelPage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            UpdateChannelBannerSize();
        }

        private void ChannelPage_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            UpdateChannelBannerSize();
        }

        private async System.Threading.Tasks.Task LoadChannelAsync(bool forceRefresh)
        {
            if (forceRefresh)
            {
                ClearChannelCache(Channel.Id);
            }

            if (!forceRefresh && TryLoadFromCache())
            {
                ClearLoading();
                Bindings.Update();
                return;
            }

            SetLoading("Loading channel uploads...");
            StatusText = "Loading channel uploads...";
            Bindings.Update();

            try
            {
                ChannelItem? updatedChannel =
                    await ServiceLocator.YouTube.GetChannelAsync(Channel.Id);

                if (updatedChannel != null)
                {
                    Channel = updatedChannel;
                    Bindings.Update();
                    UpdateBannerVisibility();
                }

                await LoadChannelSubscriberTextAsync();

                bool isSubscribed =
                    await UpdateSubscriptionButtonAsync();

                // Fetch a wider candidate range, then show uploads only.
                // Many channels mix Shorts/lives into their latest items, so asking for
                // only 24 total items can leave the Uploads tab with just a few videos.
                List<VideoItem> videos =
                    await ServiceLocator.YouTube.GetChannelVideosAsync(
                        Channel.Id,
                        Math.Max(96, requestedUploadCount * 4));

                ReplaceVideos(
                    videos,
                    includeShorts: false,
                    includeLivestreams: false);

                SaveToCache(isSubscribed);
            }
            catch (Exception ex)
            {
                ClearLoading();
                StatusText = $"Could not load this channel: {ex.Message}";
                Bindings.Update();
                return;
            }

            ClearLoading();
            StatusText = FormatLoadedStatusText();
            Bindings.Update();
        }

        private async void LoadMoreButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoadingMore)
                return;

            isLoadingMore = true;

            int selectedIndex = ChannelContentPivot?.SelectedIndex ?? 0;

            if (selectedIndex == 1)
            {
                requestedShortCount += 24;
                SetLoading("Loading more shorts...");
                StatusText = "Loading more shorts...";
                Bindings.Update();

                int previousCount = ShortVideos.Count;
                await LoadShortsAsync(forceRefresh: true);

                if (ShortVideos.Count <= previousCount)
                {
                    StatusText =
                        "No more shorts were returned for this channel.\n" +
                        FormatLoadedStatusText();
                    Bindings.Update();
                }

                isLoadingMore = false;
                return;
            }

            if (selectedIndex == 2)
            {
                requestedLivestreamCount += 24;
                SetLoading("Loading more livestreams...");
                StatusText = "Loading more livestreams...";
                Bindings.Update();

                int previousCount = LivestreamVideos.Count;
                await LoadLivestreamsAsync(forceRefresh: true);

                if (LivestreamVideos.Count <= previousCount)
                {
                    StatusText =
                        "No more livestreams were returned for this channel.\n" +
                        FormatLoadedStatusText();
                    Bindings.Update();
                }

                isLoadingMore = false;
                return;
            }

            requestedUploadCount += 24;
            SetLoading("Loading more uploads...");
            StatusText = "Loading more uploads...";
            Bindings.Update();

            int previousUploadCount = UploadVideos.Count;
            await LoadChannelAsync(true);

            if (UploadVideos.Count <= previousUploadCount)
            {
                StatusText =
                    "No more uploads were returned for this channel.\n" +
                    FormatLoadedStatusText();

                Bindings.Update();
            }

            isLoadingMore = false;
        }

        private async void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Channel.Id))
                return;

            SetRefreshLoading(true);

            try
            {
                requestedUploadCount = Math.Max(requestedUploadCount, 24);
                requestedShortCount = Math.Max(requestedShortCount, 24);
                requestedLivestreamCount = Math.Max(requestedLivestreamCount, 24);
                shortsLoaded = false;
                livestreamsLoaded = false;
                allVideos.Clear();
                UploadVideos.Clear();
                ShortVideos.Clear();
                LivestreamVideos.Clear();
                SetLoading("Refreshing channel...");
                StatusText = "Refreshing channel...";
                Bindings.Update();
                await LoadChannelAsync(true);
            }
            finally
            {
                SetRefreshLoading(false);
            }
        }

        private async void ChannelContentPivot_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isLoadingTab)
                return;

            if (ChannelContentPivot == null ||
                string.IsNullOrWhiteSpace(Channel.Id))
            {
                return;
            }

            if (ChannelContentPivot.SelectedIndex == 1 &&
                !shortsLoaded)
            {
                await LoadShortsAsync(false);
            }
            else if (ChannelContentPivot.SelectedIndex == 2 &&
                !livestreamsLoaded)
            {
                await LoadLivestreamsAsync(false);
            }
            else
            {
                StatusText = FormatLoadedStatusText();
                Bindings.Update();
            }
        }

        private async System.Threading.Tasks.Task LoadShortsAsync(bool forceRefresh)
        {
            if (!forceRefresh &&
                shortsLoaded &&
                ShortVideos.Count > 0)
            {
                StatusText = FormatLoadedStatusText();
                Bindings.Update();
                return;
            }

            isLoadingTab = true;
            SetLoading("Loading shorts...");
            StatusText = "Loading shorts...";
            Bindings.Update();

            try
            {
                List<VideoItem> videos =
                    await ServiceLocator.YouTube.GetChannelVideosAsync(
                        Channel.Id,
                        Math.Max(72, requestedShortCount * 4));

                List<VideoItem> shorts =
                    videos
                        .Where(IsLikelyShort)
                        .GroupBy(video => video.Id)
                        .Select(group => group.First())
                        .OrderByDescending(video => video.PublishedAtSort ?? DateTimeOffset.MinValue)
                        .Take(requestedShortCount)
                        .ToList();

                ReplaceCollection(ShortVideos, shorts);
                MergeIntoAllVideos(shorts);
                shortsLoaded = true;

                SaveToCache(
                    SubscribeButton?.Content?.ToString() == "Subscribed");

                StatusText = FormatLoadedStatusText();
            }
            catch (Exception ex)
            {
                StatusText = $"Could not load shorts: {ex.Message}";
            }

            isLoadingTab = false;
            ClearLoading();
            Bindings.Update();
        }

        private async System.Threading.Tasks.Task LoadLivestreamsAsync(bool forceRefresh)
        {
            if (!forceRefresh &&
                livestreamsLoaded &&
                LivestreamVideos.Count > 0)
            {
                StatusText = FormatLoadedStatusText();
                Bindings.Update();
                return;
            }

            isLoadingTab = true;
            SetLoading("Loading livestreams...");
            StatusText = "Loading livestreams...";
            Bindings.Update();

            try
            {
                List<VideoItem> videos =
                    await ServiceLocator.YouTube.GetChannelVideosAsync(
                        Channel.Id,
                        Math.Max(72, requestedLivestreamCount * 4));

                List<VideoItem> livestreams =
                    videos
                        .Where(IsLikelyLivestream)
                        .GroupBy(video => video.Id)
                        .Select(group => group.First())
                        .OrderByDescending(video => video.PublishedAtSort ?? DateTimeOffset.MinValue)
                        .Take(requestedLivestreamCount)
                        .ToList();

                ReplaceCollection(LivestreamVideos, livestreams);
                MergeIntoAllVideos(livestreams);
                livestreamsLoaded = true;

                SaveToCache(
                    SubscribeButton?.Content?.ToString() == "Subscribed");

                StatusText = FormatLoadedStatusText();
            }
            catch (Exception ex)
            {
                StatusText = $"Could not load livestreams: {ex.Message}";
            }

            isLoadingTab = false;
            ClearLoading();
            Bindings.Update();
        }

        private async System.Threading.Tasks.Task LoadChannelSubscriberTextAsync()
        {
            if (string.IsNullOrWhiteSpace(Channel.Id) ||
                string.IsNullOrWhiteSpace(AppSettings.YouTubeApiKey))
            {
                ChannelSubscriberText = "";
                ChannelSubscriberVisibility = Visibility.Collapsed;
                Bindings.Update();
                return;
            }

            try
            {
                string requestUri =
                    "https://www.googleapis.com/youtube/v3/channels" +
                    "?part=statistics" +
                    $"&id={Uri.EscapeDataString(Channel.Id)}" +
                    $"&key={Uri.EscapeDataString(AppSettings.YouTubeApiKey)}";

                using HttpResponseMessage response =
                    await ChannelMetadataHttpClient.GetAsync(requestUri);

                if (!response.IsSuccessStatusCode)
                    return;

                await using System.IO.Stream stream =
                    await response.Content.ReadAsStreamAsync();

                using JsonDocument document =
                    await JsonDocument.ParseAsync(stream);

                if (!document.RootElement.TryGetProperty("items", out JsonElement items) ||
                    items.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                JsonElement channel =
                    items.EnumerateArray().FirstOrDefault();

                if (channel.ValueKind != JsonValueKind.Object ||
                    !channel.TryGetProperty("statistics", out JsonElement statistics))
                {
                    return;
                }

                bool hiddenSubscriberCount =
                    statistics.TryGetProperty("hiddenSubscriberCount", out JsonElement hiddenElement) &&
                    hiddenElement.ValueKind == JsonValueKind.True;

                if (hiddenSubscriberCount ||
                    !statistics.TryGetProperty("subscriberCount", out JsonElement subscriberElement))
                {
                    ChannelSubscriberText = "";
                    ChannelSubscriberVisibility = Visibility.Collapsed;
                    Bindings.Update();
                    return;
                }

                string subscriberCount =
                    subscriberElement.ValueKind == JsonValueKind.String
                        ? subscriberElement.GetString() ?? ""
                        : subscriberElement.GetRawText();

                if (!ulong.TryParse(
                        subscriberCount,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out ulong count))
                {
                    return;
                }

                ChannelSubscriberText =
                    $"{FormatCompactSubscriberCount(count)} subscribers";

                ChannelSubscriberVisibility = Visibility.Visible;
                Bindings.Update();
            }
            catch (Exception ex) when (ex is HttpRequestException ||
                ex is TaskCanceledException ||
                ex is JsonException)
            {
                // Subscriber count is optional polish. Never block the channel page
                // if YouTube metadata or quota is unavailable.
            }
        }

        private static string FormatCompactSubscriberCount(ulong count)
        {
            return count switch
            {
                >= 1_000_000_000 => $"{TrimCompactCount(count / 1_000_000_000d)}B",
                >= 1_000_000 => $"{TrimCompactCount(count / 1_000_000d)}M",
                >= 1_000 => $"{TrimCompactCount(count / 1_000d)}K",
                _ => count.ToString("N0", CultureInfo.InvariantCulture)
            };
        }

        private static string TrimCompactCount(double value)
        {
            return value.ToString(value >= 10 ? "0.#" : "0.##", CultureInfo.InvariantCulture);
        }

        private void SetLoading(string message)
        {
            LoadingText = message;
            LoadingVisibility = Visibility.Visible;
        }

        private void ClearLoading()
        {
            LoadingVisibility = Visibility.Collapsed;
        }

        private static void EnsurePersistentChannelCacheLoaded()
        {
            if (triedPersistentChannelCache)
                return;

            triedPersistentChannelCache = true;

            Dictionary<string, ChannelPageCacheEntry>? cache =
                PersistentCacheService.Load<Dictionary<string, ChannelPageCacheEntry>>(
                    ChannelCacheFileName);

            if (cache == null || cache.Count == 0)
                return;

            ChannelCache.Clear();

            foreach (KeyValuePair<string, ChannelPageCacheEntry> item in cache)
            {
                if (!string.IsNullOrWhiteSpace(item.Key) &&
                    item.Value?.Videos != null)
                {
                    ChannelCache[item.Key] = item.Value;
                }
            }
        }

        private bool TryLoadFromCache()
        {
            EnsurePersistentChannelCacheLoaded();
            if (string.IsNullOrWhiteSpace(Channel.Id) ||
                !ChannelCache.TryGetValue(Channel.Id, out ChannelPageCacheEntry? cache) ||
                cache.Videos.Count == 0 ||
                cache.RequestedUploadCount < requestedUploadCount)
            {
                return false;
            }

            Channel = cache.Channel;

            requestedShortCount = Math.Max(requestedShortCount, cache.RequestedShortCount);
            requestedLivestreamCount = Math.Max(requestedLivestreamCount, cache.RequestedLivestreamCount);
            shortsLoaded = cache.ShortsLoaded;
            livestreamsLoaded = cache.LivestreamsLoaded;

            ReplaceVideos(
                cache.Videos,
                includeShorts: shortsLoaded,
                includeLivestreams: livestreamsLoaded);

            StatusText = FormatLoadedStatusText(cache.LastLoadedAt);
            UpdateBannerVisibility();

            if (cache.IsSubscribed.HasValue)
            {
                ApplySubscriptionButtonState(cache.IsSubscribed.Value);
            }

            _ = LoadChannelSubscriberTextAsync();

            return true;
        }

        private void SaveToCache(bool isSubscribed)
        {
            if (string.IsNullOrWhiteSpace(Channel.Id))
                return;

            ChannelCache[Channel.Id] = new ChannelPageCacheEntry
            {
                Channel = Channel,
                Videos = allVideos.ToList(),
                RequestedUploadCount = requestedUploadCount,
                RequestedShortCount = requestedShortCount,
                RequestedLivestreamCount = requestedLivestreamCount,
                ShortsLoaded = shortsLoaded,
                LivestreamsLoaded = livestreamsLoaded,
                IsSubscribed = isSubscribed,
                LastLoadedAt = DateTimeOffset.Now
            };

            PersistentCacheService.Save(
                ChannelCacheFileName,
                ChannelCache);
        }

        private static void ClearChannelCache(string channelId)
        {
            EnsurePersistentChannelCacheLoaded();

            if (string.IsNullOrWhiteSpace(channelId))
                return;

            if (ChannelCache.Remove(channelId))
            {
                PersistentCacheService.Save(
                    ChannelCacheFileName,
                    ChannelCache);
            }
        }

        private void SetRefreshLoading(bool isRefreshing)
        {
            IsRefreshing = isRefreshing;
            RefreshIconVisibility = isRefreshing
                ? Visibility.Collapsed
                : Visibility.Visible;
            RefreshProgressVisibility = isRefreshing
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (RefreshButton != null)
            {
                RefreshButton.IsEnabled = !isRefreshing;
            }

            Bindings.Update();
        }

        private string FormatLoadedStatusText(DateTimeOffset? lastLoadedAt = null)
        {
            string selectedSection =
                (ChannelContentPivot?.SelectedIndex ?? 0) switch
                {
                    1 => "shorts",
                    2 => "livestreams",
                    _ => "uploads"
                };

            int selectedCount =
                (ChannelContentPivot?.SelectedIndex ?? 0) switch
                {
                    1 => ShortVideos.Count,
                    2 => LivestreamVideos.Count,
                    _ => UploadVideos.Count
                };

            if (selectedCount == 0)
            {
                string emptyText =
                    selectedSection == "uploads"
                        ? "No recent uploads loaded for this channel."
                        : $"No {selectedSection} loaded for this channel yet.";

                return emptyText + "\nLoad more to fetch additional content.";
            }

            string text =
                $"Currently loaded: {UploadVideos.Count} uploads";

            if (shortsLoaded)
            {
                text += $" • {ShortVideos.Count} shorts";
            }
            else
            {
                text += " • Shorts load when you open the Shorts tab";
            }

            if (livestreamsLoaded)
            {
                text += $" • {LivestreamVideos.Count} livestreams";
            }
            else
            {
                text += " • Livestreams load when you open the Livestreams tab";
            }

            text += "\nLoad more to fetch additional content for the selected tab.";

            if (lastLoadedAt.HasValue)
            {
                text += $" Last loaded {lastLoadedAt.Value.LocalDateTime:g}.";
            }

            return text;
        }

        private void ReplaceVideos(
            IEnumerable<VideoItem> videos,
            bool includeShorts,
            bool includeLivestreams)
        {
            List<VideoItem> normalizedVideos =
                videos
                    .Where(video => !string.IsNullOrWhiteSpace(video.Id))
                    .GroupBy(video => video.Id)
                    .Select(group => group.First())
                    .OrderByDescending(video => video.PublishedAtSort ?? DateTimeOffset.MinValue)
                    .ToList();

            MergeIntoAllVideos(normalizedVideos);

            ReplaceCollection(
                UploadVideos,
                allVideos
                    .Where(video =>
                        !IsLikelyLivestream(video) &&
                        !IsLikelyShort(video))
                    .OrderByDescending(video => video.PublishedAtSort ?? DateTimeOffset.MinValue)
                    .Take(requestedUploadCount));

            if (includeShorts)
            {
                ReplaceCollection(
                    ShortVideos,
                    allVideos
                        .Where(IsLikelyShort)
                        .OrderByDescending(video => video.PublishedAtSort ?? DateTimeOffset.MinValue)
                        .Take(requestedShortCount));
            }
            else
            {
                ShortVideos.Clear();
            }

            if (includeLivestreams)
            {
                ReplaceCollection(
                    LivestreamVideos,
                    allVideos
                        .Where(IsLikelyLivestream)
                        .OrderByDescending(video => video.PublishedAtSort ?? DateTimeOffset.MinValue)
                        .Take(requestedLivestreamCount));
            }
            else
            {
                LivestreamVideos.Clear();
            }
        }

        private void MergeIntoAllVideos(IEnumerable<VideoItem> videos)
        {
            allVideos =
                allVideos
                    .Concat(videos)
                    .Where(video => !string.IsNullOrWhiteSpace(video.Id))
                    .GroupBy(video => video.Id)
                    .Select(group => group.First())
                    .OrderByDescending(video => video.PublishedAtSort ?? DateTimeOffset.MinValue)
                    .ToList();
        }

        private static void ReplaceCollection(
            ObservableCollection<VideoItem> target,
            IEnumerable<VideoItem> videos)
        {
            target.Clear();

            foreach (VideoItem video in videos)
            {
                target.Add(video);
            }
        }

        private static bool IsLikelyShort(VideoItem video)
        {
            string title =
                video.Title ?? "";

            string category =
                video.Category ?? "";

            if (category.Equals("Shorts", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("#short", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("#shorts", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" youtube shorts", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("ytshorts", StringComparison.OrdinalIgnoreCase) ||
                title.EndsWith(" #short", StringComparison.OrdinalIgnoreCase) ||
                title.EndsWith(" #shorts", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            double durationSeconds =
                video.DurationSeconds > 0
                    ? video.DurationSeconds
                    : ParseDurationSeconds(video.Duration);

            if (durationSeconds <= 0)
                return false;

            // Always keep clear long-form/music-upload wording in Uploads even if
            // the runtime is short. This avoids moving 2-3 minute music videos,
            // lyric videos, visualizers, official audio, and performances into Shorts.
            if (LooksLikeRegularUploadTitle(title))
                return false;

            // YouTube/fallback metadata can mark normal videos as short-eligible,
            // but a short runtime makes that signal useful.
            if (video.IsShort && durationSeconds <= 90)
                return true;

            // Some channel feeds do not provide a reliable Shorts flag. Catch obvious
            // Shorts-style uploads by duration only at a conservative threshold.
            // This catches 0:13, 0:47, 1:02, 1:14 style Shorts while avoiding the
            // old under-3-minutes rule that hid normal short uploads.
            return durationSeconds <= 90;
        }

        private static bool LooksLikeRegularUploadTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            return title.Contains("official music video", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("music video", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("official video", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("official audio", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("visualizer", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("lyric video", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("lyrics", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("performance", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("live performance", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("acoustic", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("trailer", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("teaser", StringComparison.OrdinalIgnoreCase);
        }

        private static double ParseDurationSeconds(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            string[] parts =
                value
                    .Trim()
                    .Split(':', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0 || parts.Length > 3)
                return 0;

            double totalSeconds = 0;

            foreach (string part in parts)
            {
                if (!int.TryParse(part, out int parsed))
                    return 0;

                totalSeconds = totalSeconds * 60 + parsed;
            }

            return totalSeconds;
        }

        private static bool IsLikelyLivestream(VideoItem video)
        {
            if (video.IsLive || video.IsPremiere)
                return true;

            string title = video.Title ?? "";

            return
                title.Contains("livestream", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("live stream", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" live ", StringComparison.OrdinalIgnoreCase) ||
                title.EndsWith(" live", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("premiere", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateBannerVisibility()
        {
            bool hasBanner =
                !string.IsNullOrWhiteSpace(Channel.BannerImage);

            ChannelBannerImage.Source =
                hasBanner
                    ? CreateImageSource(Channel.BannerImage)
                    : null;

            ChannelThumbnailImage.Source =
                CreateImageSource(Channel.Thumbnail);

            ChannelBannerImage.Visibility =
                hasBanner
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ChannelBannerPlaceholder.Visibility =
                hasBanner
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            UpdateChannelBannerSize();
        }

        private void UpdateChannelBannerSize()
        {
            if (ChannelBanner == null)
                return;

            double availableWidth =
                ActualWidth;

            if (availableWidth <= 0 && ChannelBanner.ActualWidth > 0)
            {
                availableWidth = ChannelBanner.ActualWidth;
            }

            if (availableWidth <= 0)
                return;

            // YouTube channel art is uploaded as a 16:9 source image, but desktop
            // channel pages show the centered ultra-wide banner strip. Keep SmoothTube
            // focused on that desktop center/safe zone instead of showing the full image.
            const double desktopBannerAspectRatio = 6.05;

            double contentWidth =
                Math.Max(0, availableWidth - 80);

            double bannerHeight =
                Math.Clamp(
                    contentWidth / desktopBannerAspectRatio,
                    200,
                    280);

            ChannelBanner.Height = bannerHeight;
        }

        private static BitmapImage? CreateImageSource(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out Uri? uri))
            {
                return null;
            }

            return new BitmapImage(uri);
        }

        private void BackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async void SubscribeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SubscribeButton.IsEnabled = false;
            SubscribeButton.Content = "Subscribing...";

            bool success =
                await ServiceLocator.YouTube.SubscribeToChannelAsync(Channel.Id);

            SubscribeButton.Content =
                success
                    ? "Subscribed"
                    : "Sign in to subscribe";

            SubscribeButton.IsEnabled = !success;
        }

        private async System.Threading.Tasks.Task<bool> UpdateSubscriptionButtonAsync()
        {
            bool isSubscribed =
                await ServiceLocator.YouTube.IsSubscribedToChannelAsync(
                    Channel.Id);

            ApplySubscriptionButtonState(isSubscribed);
            return isSubscribed;
        }

        private void ApplySubscriptionButtonState(bool isSubscribed)
        {
            SubscribeButton.Content =
                isSubscribed
                    ? "Subscribed"
                    : "Subscribe";

            SubscribeButton.IsEnabled = !isSubscribed;
        }

        private void VideoCard_Tapped(
            object sender,
            TappedRoutedEventArgs e)
        {
            if (sender is Border card &&
                card.DataContext is VideoItem video)
            {
                List<VideoItem> upNext =
                    allVideos
                        .Where(item => item.Id != video.Id)
                        .Take(24)
                        .ToList();

                upNext.Insert(0, video);

                Frame.Navigate(
                    typeof(VideoPage),
                    new VideoNavigationData
                    {
                        CurrentVideo = video,
                        AllVideos = upNext
                    });
            }
        }
    }
}
