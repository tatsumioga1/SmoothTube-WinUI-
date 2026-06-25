using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SmoothTube.Models;
using SmoothTube.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmoothTube
{
    public sealed partial class SubscriptionsPage : Page
    {
        private const string SubscriptionsCacheFileName = "subscriptions-cache.json";

        private sealed class SubscriptionsCache
        {
            public List<VideoItem> Uploads { get; set; } = [];
            public List<VideoItem> Broadcasts { get; set; } = [];
            public int UploadDays { get; set; }
            public bool BroadcastsLoaded { get; set; }
            public bool BroadcastQuotaExhausted { get; set; }
            public bool UploadsIncludeShorts { get; set; }
            public DateTimeOffset? UploadsRefreshedAt { get; set; }
            public DateTimeOffset? BroadcastsRefreshedAt { get; set; }
        }

        private static readonly List<VideoItem> CachedUploads = [];
        private static readonly List<VideoItem> CachedBroadcasts = [];
        private static int cachedUploadDays;
        private static bool cachedBroadcastsLoaded;
        private static bool cachedBroadcastQuotaExhausted;
        private static bool cachedUploadsIncludeShorts;
        private static DateTimeOffset? cachedUploadsRefreshedAt;
        private static DateTimeOffset? cachedBroadcastsRefreshedAt;
        private static bool triedPersistentSubscriptionsCache;

        public ObservableCollection<VideoItem> Videos { get; } = [];

        public ObservableCollection<VideoItem> PremiereVideos { get; } = [];

        public ObservableCollection<VideoItem> LivestreamVideos { get; } = [];

        private readonly List<VideoItem> loadedUploads = [];

        private readonly List<VideoItem> loadedBroadcasts = [];

        public string StatusText { get; set; } =
            "Sign in from Settings to load your YouTube subscriptions.";

        public Visibility LoadMoreVisibility { get; set; } = Visibility.Visible;

        public Visibility RefreshIconVisibility { get; set; } = Visibility.Visible;

        public Visibility RefreshProgressVisibility { get; set; } = Visibility.Collapsed;

        public bool IsRefreshing { get; set; }

        private bool isLoaded;
        private bool isLoading;
        private bool broadcastsLoaded;
        private bool broadcastsLoading;
        private int loadedUploadDays = 30;
        private int currentUploadDisplayLimit = InitialUploadLimit;
        private CancellationTokenSource? loadCancellation;

        private const int InitialUploadLimit = 24;
        private const int InitialUploadLookbackDays = 30;

        public SubscriptionsPage()
        {
            InitializeComponent();

            Loaded += SubscriptionsPage_Loaded;
        }

        private async void SubscriptionsPage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (!ServiceLocator.GoogleOAuth.IsSignedIn)
            {
                StatusText =
                    "Sign in from Settings to load your YouTube subscriptions.";

                Bindings.Update();
                return;
            }

            isLoaded = true;

            EnsurePersistentSubscriptionsCacheLoaded();

            if (TryLoadFromPageCache())
            {
                ApplyVisibleFilters();
                return;
            }

            await LoadVideosAsync(false);
        }

        private async void Filters_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (!isLoaded)
                return;

            if (ReferenceEquals(sender, IncludeShortsSwitch))
            {
                if (IncludeShortsSwitch.IsOn &&
                    CachedUploads.Count > 0 &&
                    !cachedUploadsIncludeShorts)
                {
                    await LoadVideosAsync(true);
                    return;
                }

                ApplyVisibleFilters();
            }
        }

        private async void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoading)
                return;

            SetRefreshLoading(true);

            try
            {
                await LoadVideosAsync(true);
            }
            finally
            {
                SetRefreshLoading(false);
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

        private async void LoadMoreButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoading)
                return;

            loadedUploadDays++;
            currentUploadDisplayLimit += 10;

            await LoadUploadRangeAsync(
                loadedUploadDays,
                true,
                currentUploadDisplayLimit,
                loadCancellation?.Token ?? CancellationToken.None);

            SaveUploadsToPageCache();
            ApplyVisibleFilters();
        }

        private async Task LoadVideosAsync(bool forceRefresh)
        {
            loadCancellation?.Cancel();
            loadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = loadCancellation.Token;

            EnsurePersistentSubscriptionsCacheLoaded();

            if (forceRefresh)
            {
                // Keep the existing visible window as the refresh seed.
                // New RSS entries replace the top of the list and the oldest visible
                // items fall off after we re-cap to currentUploadDisplayLimit.
                ServiceLocator.YouTube.ClearSubscribedVideoCache();
            }
            else if (TryLoadFromPageCache())
            {
                ApplyVisibleFilters();
                return;
            }

            loadedUploadDays = InitialUploadLookbackDays;
            currentUploadDisplayLimit = Math.Max(InitialUploadLimit, Videos.Count > 0 ? Videos.Count : CachedUploads.Count);

            List<VideoItem> previousVisibleUploads =
                loadedUploads.Count > 0
                    ? loadedUploads.ToList()
                    : CachedUploads.ToList();

            loadedUploads.Clear();
            loadedBroadcasts.Clear();

            Videos.Clear();
            PremiereVideos.Clear();
            LivestreamVideos.Clear();

            broadcastsLoaded = false;
            broadcastsLoading = false;

            StatusText = forceRefresh
                ? IncludeShortsSwitch.IsOn
                    ? "Refreshing latest subscription uploads..."
                    : "Refreshing latest long-form subscription uploads..."
                : IncludeShortsSwitch.IsOn
                    ? "Loading recent subscription uploads..."
                    : "Loading recent long-form subscription uploads...";

            Bindings.Update();

            try
            {
                await LoadUploadRangeAsync(
                    loadedUploadDays,
                    false,
                    currentUploadDisplayLimit,
                    cancellationToken,
                    previousVisibleUploads);

                SaveUploadsToPageCache();
                ApplyVisibleFilters();

                // Livestreams/premieres are intentionally not auto-loaded here.
                // They use the expensive broadcast scan and load only when those tabs are opened.
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception)
            {
                if (CachedUploads.Count > 0 || CachedBroadcasts.Count > 0)
                {
                    TryLoadFromPageCache();
                    ApplyVisibleFilters();
                    StatusText =
                        "Refresh failed. Showing cached subscription results. Try again in a moment.\n" +
                        FormatStatusText(false);
                }
                else
                {
                    StatusText =
                        "Could not load subscription uploads. Try Refresh in a moment.";
                }

                Bindings.Update();
            }
        }

        private async Task LoadUploadRangeAsync(
            int days,
            bool append,
            int? displayLimit,
            CancellationToken cancellationToken,
            List<VideoItem>? refreshSeed = null)
        {
            if (isLoading)
                return;

            isLoading = true;
            LoadMoreButton.IsEnabled = false;

            StatusText = append
                ? "Loading more recent uploads..."
                : IncludeShortsSwitch.IsOn
                    ? "Loading recent subscription uploads..."
                    : "Loading recent long-form subscription uploads...";

            Bindings.Update();

            try
            {
                int previousCount = loadedUploads.Count;

                await foreach (List<VideoItem> batch in
                    ServiceLocator.YouTube.GetSubscribedVideoBatchesAsync(
                        days,
                        IncludeShortsSwitch.IsOn,
                        cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    List<VideoItem> freshVideos =
                        batch
                            // Keep feed-discovered completed livestreams/premieres in the recent flow.
                            // Active/upcoming discovery still stays in the dedicated tabs.
                            .Where(video => IncludeShortsSwitch.IsOn || !IsLikelyShort(video))
                            .OrderByDescending(GetPublishedAtSort)
                            .ToList();

                    int targetLimit =
                        Math.Max(
                            InitialUploadLimit,
                            displayLimit ?? currentUploadDisplayLimit);

                    if (append)
                    {
                        List<VideoItem> window =
                            loadedUploads
                                .Concat(freshVideos)
                                .Where(video => IsWithinRecentWindow(video, days))
                                .GroupBy(video => video.Id, StringComparer.OrdinalIgnoreCase)
                                .Select(group =>
                                    group
                                        .OrderByDescending(GetPublishedAtSort)
                                        .First())
                                .OrderByDescending(GetPublishedAtSort)
                                .Take(targetLimit)
                                .ToList();

                        loadedUploads.Clear();
                        loadedUploads.AddRange(window);
                    }
                    else
                    {
                        // Refresh behavior:
                        // fresh RSS entries + previous visible cache -> globally sorted fixed window.
                        // If 2 new videos appear, the oldest 2 in the visible window drop off.
                        HashSet<string> freshIds =
                            freshVideos
                                .Select(video => video.Id)
                                .Where(id => !string.IsNullOrWhiteSpace(id))
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        List<VideoItem> window =
                            freshVideos
                                .Concat(refreshSeed ?? [])
                                .Where(video => IsWithinRecentWindow(video, days))
                                .GroupBy(video => video.Id, StringComparer.OrdinalIgnoreCase)
                                .Select(group =>
                                    group
                                        .OrderByDescending(video => freshIds.Contains(video.Id))
                                        .ThenByDescending(GetPublishedAtSort)
                                        .First())
                                .OrderByDescending(GetPublishedAtSort)
                                .Take(targetLimit)
                                .ToList();

                        loadedUploads.Clear();
                        loadedUploads.AddRange(window);
                    }

                    currentUploadDisplayLimit =
                        Math.Max(currentUploadDisplayLimit, loadedUploads.Count);

                    ApplyVisibleFilters(stillLoading: true);

                    if (append)
                    {
                        break;
                    }
                }

                if (append && loadedUploads.Count <= previousCount)
                {
                    StatusText =
                        "No more uploads were returned right now.\n" +
                        FormatStatusText(false);

                    Bindings.Update();
                }
            }
            finally
            {
                isLoading = false;
                LoadMoreButton.IsEnabled = true;
            }
        }

        private async Task LoadBroadcastsAsync(CancellationToken cancellationToken)
        {
            if (broadcastsLoading || broadcastsLoaded)
                return;

            if (cachedBroadcastQuotaExhausted)
            {
                loadedBroadcasts.Clear();
                loadedBroadcasts.AddRange(CachedBroadcasts);
                ApplyVisibleFilters();

                StatusText = FormatBroadcastQuotaStatus();
                Bindings.Update();
                return;
            }

            if (CachedBroadcasts.Count > 0 || cachedBroadcastsLoaded)
            {
                loadedBroadcasts.Clear();
                loadedBroadcasts.AddRange(CachedBroadcasts);
                broadcastsLoaded = cachedBroadcastsLoaded;
                ApplyVisibleFilters();
                return;
            }

            broadcastsLoading = true;

            StatusText = "Checking live and upcoming subscriptions...";
            Bindings.Update();

            try
            {
                await foreach (List<VideoItem> batch in
                    ServiceLocator.YouTube.GetSubscribedBroadcastBatchesAsync(
                        cancellationToken: cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    MergeVideos(loadedBroadcasts, batch);
                    SaveBroadcastsToPageCache(false);

                    ApplyVisibleFilters(true);
                }

                if (ServiceLocator.YouTube.IsSearchQuotaExhausted)
                {
                    cachedBroadcastQuotaExhausted = true;
                    broadcastsLoaded = false;
                    SaveBroadcastsToPageCache(false);
                    ApplyVisibleFilters();

                    StatusText = FormatBroadcastQuotaStatus();
                    Bindings.Update();
                    return;
                }

                broadcastsLoaded = true;
                cachedBroadcastQuotaExhausted = false;
                SaveBroadcastsToPageCache(true);
                ApplyVisibleFilters();
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                ex is System.Runtime.InteropServices.COMException)
            {
                StatusText =
                    "Live and premiere checks failed. Try Refresh in a moment.\n" +
                    FormatStatusText(false);

                Bindings.Update();
            }
            finally
            {
                broadcastsLoading = false;
            }
        }

        private async void SubscriptionsPivot_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!isLoaded || SubscriptionsPivot.SelectedIndex == 0)
                return;

            await LoadBroadcastsAsync(
                loadCancellation?.Token ?? CancellationToken.None);
        }

        private void VideoCard_Tapped(
            object sender,
            TappedRoutedEventArgs e)
        {
            if (sender is Border card &&
                card.DataContext is VideoItem video)
            {
                OpenVideo(video);
            }
        }

        private void ApplyVisibleFilters(bool stillLoading = false)
        {
            List<VideoItem> visibleVideos =
                loadedUploads
                    .Concat(loadedBroadcasts)
                    .Where(video => IsWithinRecentWindow(video, loadedUploadDays))
                    .Where(video => IncludeShortsSwitch.IsOn || !IsLikelyShort(video))
                    .GroupBy(video => video.Id)
                    .Select(group => group.First())
                    .OrderByDescending(GetPublishedAtSort)
                    .ToList();

            Videos.Clear();
            PremiereVideos.Clear();
            LivestreamVideos.Clear();

            foreach (VideoItem video in visibleVideos)
            {
                Videos.Add(video);
            }

            foreach (VideoItem video in visibleVideos
                .Where(video => video.IsPremiere))
            {
                PremiereVideos.Add(video);
            }

            foreach (VideoItem video in visibleVideos
                .Where(video => video.IsLive))
            {
                LivestreamVideos.Add(video);
            }

            StatusText =
                Videos.Count == 0 && PremiereVideos.Count == 0 && LivestreamVideos.Count == 0
                    ? stillLoading
                        ? "Loading subscriptions..."
                        : "No recent videos loaded for these filters.\n" + FormatStatusText(false)
                    : FormatStatusText(stillLoading);

            Bindings.Update();
        }

        private bool TryLoadFromPageCache()
        {
            if (CachedUploads.Count == 0 && CachedBroadcasts.Count == 0)
                return false;

            if (IncludeShortsSwitch.IsOn && CachedUploads.Count > 0 && !cachedUploadsIncludeShorts)
                return false;

            loadedUploadDays = InitialUploadLookbackDays;
            currentUploadDisplayLimit = Math.Max(InitialUploadLimit, CachedUploads.Count);

            loadedUploads.Clear();
            loadedUploads.AddRange(
                CachedUploads
                    .Where(video => IsWithinRecentWindow(video, loadedUploadDays))
                    .GroupBy(video => video.Id)
                    .Select(group => group.First())
                    .OrderByDescending(GetPublishedAtSort));

            loadedBroadcasts.Clear();
            loadedBroadcasts.AddRange(
                CachedBroadcasts
                    .Where(video => IsWithinRecentWindow(video, loadedUploadDays))
                    .GroupBy(video => video.Id)
                    .Select(group => group.First())
                    .OrderByDescending(GetPublishedAtSort));

            broadcastsLoaded = cachedBroadcastsLoaded;

            return true;
        }

        private static void EnsurePersistentSubscriptionsCacheLoaded()
        {
            if (triedPersistentSubscriptionsCache)
                return;

            triedPersistentSubscriptionsCache = true;

            SubscriptionsCache? cache =
                PersistentCacheService.Load<SubscriptionsCache>(
                    SubscriptionsCacheFileName);

            if (cache == null)
                return;

            CachedUploads.Clear();
            CachedUploads.AddRange(cache.Uploads ?? []);

            CachedBroadcasts.Clear();
            CachedBroadcasts.AddRange(cache.Broadcasts ?? []);

            cachedUploadDays = cache.UploadDays;
            cachedBroadcastsLoaded = cache.BroadcastsLoaded;
            cachedBroadcastQuotaExhausted = cache.BroadcastQuotaExhausted;
            cachedUploadsIncludeShorts = cache.UploadsIncludeShorts;
            cachedUploadsRefreshedAt = cache.UploadsRefreshedAt;
            cachedBroadcastsRefreshedAt = cache.BroadcastsRefreshedAt;
        }

        private static void SavePersistentSubscriptionsCache()
        {
            if (CachedUploads.Count == 0 && CachedBroadcasts.Count == 0)
                return;

            PersistentCacheService.Save(
                SubscriptionsCacheFileName,
                new SubscriptionsCache
                {
                    Uploads = CachedUploads.ToList(),
                    Broadcasts = CachedBroadcasts.ToList(),
                    UploadDays = cachedUploadDays,
                    BroadcastsLoaded = cachedBroadcastsLoaded,
                    BroadcastQuotaExhausted = cachedBroadcastQuotaExhausted,
                    UploadsIncludeShorts = cachedUploadsIncludeShorts,
                    UploadsRefreshedAt = cachedUploadsRefreshedAt,
                    BroadcastsRefreshedAt = cachedBroadcastsRefreshedAt
                });
        }

        private void SaveUploadsToPageCache()
        {
            int cacheLimit =
                Math.Max(
                    InitialUploadLimit,
                    currentUploadDisplayLimit);

            CachedUploads.Clear();
            CachedUploads.AddRange(
                loadedUploads
                    .Where(video => IsWithinRecentWindow(video, Math.Max(InitialUploadLookbackDays, loadedUploadDays)))
                    .GroupBy(video => video.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderByDescending(GetPublishedAtSort)
                    .Take(cacheLimit));

            cachedUploadDays = loadedUploadDays;
            cachedUploadsIncludeShorts = IncludeShortsSwitch.IsOn;
            cachedUploadsRefreshedAt = DateTimeOffset.Now;
            SavePersistentSubscriptionsCache();
        }

        private void SaveBroadcastsToPageCache(bool completed)
        {
            CachedBroadcasts.Clear();
            CachedBroadcasts.AddRange(loadedBroadcasts);
            cachedBroadcastsLoaded = completed;
            cachedBroadcastsRefreshedAt = DateTimeOffset.Now;
            SavePersistentSubscriptionsCache();
        }

        private static void ClearUploadsCache(bool clearPersistent = false)
        {
            CachedUploads.Clear();
            cachedUploadDays = 0;
            cachedUploadsIncludeShorts = false;
            cachedUploadsRefreshedAt = null;

            if (clearPersistent)
            {
                if (CachedBroadcasts.Count > 0)
                {
                    SavePersistentSubscriptionsCache();
                }
                else
                {
                    PersistentCacheService.Clear(SubscriptionsCacheFileName);
                }
            }
        }

        private static void ClearPageCache(bool clearPersistent = false)
        {
            CachedUploads.Clear();
            CachedBroadcasts.Clear();
            cachedUploadDays = 0;
            cachedBroadcastsLoaded = false;
            cachedBroadcastQuotaExhausted = false;
            cachedUploadsIncludeShorts = false;
            cachedUploadsRefreshedAt = null;
            cachedBroadcastsRefreshedAt = null;

            if (clearPersistent)
            {
                PersistentCacheService.Clear(SubscriptionsCacheFileName);
            }
        }

        private static bool IsWithinRecentWindow(VideoItem video, int maxAgeDays)
        {
            if (video.IsLive || video.IsPremiere)
            {
                // Current/upcoming broadcasts use their own tabs, but if a broadcast
                // is already known and recent, keep it available for those tabs.
            }

            DateTime publishedAt = GetPublishedAtSort(video);

            if (publishedAt == DateTime.MinValue)
            {
                return false;
            }

            DateTime minDate =
                DateTime.Now.Date.AddDays(-Math.Max(1, maxAgeDays));

            return publishedAt >= minDate;
        }

        private static bool IsLikelyShort(VideoItem video)
        {
            if (video.IsShort)
                return true;

            string title = video.Title ?? "";

            bool titleLooksShort =
                title.Contains("#short", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" shorts", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" short ", StringComparison.OrdinalIgnoreCase) ||
                title.EndsWith(" short", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("ytshorts", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(video.Duration))
                return titleLooksShort;

            string[] parts = video.Duration.Split(':');

            if (parts.Length == 1 &&
                int.TryParse(parts[0], out int secondsOnly))
            {
                return secondsOnly <= 180 || titleLooksShort;
            }

            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int minutes) &&
                int.TryParse(parts[1], out int seconds))
            {
                int totalSeconds = minutes * 60 + seconds;

                return titleLooksShort || totalSeconds <= 180;
            }

            return titleLooksShort;
        }

        private string FormatBroadcastQuotaStatus()
        {
            string cacheText =
                CachedBroadcasts.Count > 0
                    ? " Showing cached live/premiere results if available."
                    : " No cached live/premiere results are available yet.";

            return
                "Live and premiere results may be partial because the YouTube Search API quota has been reached." +
                cacheText +
                " Try again after the quota resets.\n" +
                FormatStatusText(false);
        }

        private string FormatStatusText(bool isLoading)
        {
            string loadingSuffix = isLoading ? "..." : "";

            string text =
                $"Currently loaded: {Videos.Count} recent uploads • {PremiereVideos.Count} premieres • {LivestreamVideos.Count} livestreams{loadingSuffix}\n" +
                "Load more to fetch additional content.";

            if (!IncludeShortsSwitch.IsOn)
            {
                text += " Shorts are hidden. Turn on Shorts to include them.";
            }

            if (cachedUploadsRefreshedAt.HasValue)
            {
                text += $" Last refreshed {cachedUploadsRefreshedAt.Value.LocalDateTime:g}.";
            }

            return text;
        }

        private static void MergeVideos(
            List<VideoItem> target,
            IEnumerable<VideoItem> videos)
        {
            foreach (VideoItem video in videos)
            {
                if (string.IsNullOrWhiteSpace(video.Id))
                    continue;

                int existingIndex =
                    target.FindIndex(existingVideo =>
                        string.Equals(
                            existingVideo.Id,
                            video.Id,
                            StringComparison.OrdinalIgnoreCase));

                if (existingIndex >= 0)
                {
                    target[existingIndex] = MergeVideoMetadata(target[existingIndex], video);
                    continue;
                }

                target.Add(video);
            }

            target.Sort((left, right) =>
                GetPublishedAtSort(right).CompareTo(GetPublishedAtSort(left)));
        }

        private static VideoItem MergeVideoMetadata(VideoItem existing, VideoItem fresh)
        {
            existing.Title = string.IsNullOrWhiteSpace(fresh.Title) ? existing.Title : fresh.Title;
            existing.Channel = string.IsNullOrWhiteSpace(fresh.Channel) ? existing.Channel : fresh.Channel;
            existing.ChannelId = string.IsNullOrWhiteSpace(fresh.ChannelId) ? existing.ChannelId : fresh.ChannelId;
            existing.Views = string.IsNullOrWhiteSpace(fresh.Views) ? existing.Views : fresh.Views;
            existing.Duration = string.IsNullOrWhiteSpace(fresh.Duration) ? existing.Duration : fresh.Duration;
            existing.PublishedAt = string.IsNullOrWhiteSpace(fresh.PublishedAt) ? existing.PublishedAt : fresh.PublishedAt;
            existing.PublishedAtSort = fresh.PublishedAtSort ?? existing.PublishedAtSort;
            existing.Thumbnail = string.IsNullOrWhiteSpace(fresh.Thumbnail) ? existing.Thumbnail : fresh.Thumbnail;
            existing.Description = string.IsNullOrWhiteSpace(fresh.Description) ? existing.Description : fresh.Description;
            existing.IsEmbeddable = fresh.IsEmbeddable || existing.IsEmbeddable;
            existing.IsLive = fresh.IsLive;
            existing.IsPremiere = fresh.IsPremiere;
            existing.IsShort = fresh.IsShort || existing.IsShort;
            existing.Category = string.IsNullOrWhiteSpace(fresh.Category) ? existing.Category : fresh.Category;
            return existing;
        }

        private static DateTime ParsePublishedAt(string value)
        {
            return DateTime.TryParse(
                value,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AssumeLocal,
                out DateTime result)
                    ? result
                    : DateTime.MinValue;
        }

        private static DateTime GetPublishedAtSort(VideoItem video)
        {
            return video.PublishedAtSort?.LocalDateTime ??
                ParsePublishedAt(video.PublishedAt);
        }

        private void VideosGrid_ItemClick(
            object sender,
            ItemClickEventArgs e)
        {
            if (e.ClickedItem is VideoItem video)
            {
                OpenVideo(video);
            }
        }

        private void OpenVideo(VideoItem video)
        {
            List<VideoItem> upNext =
                loadedUploads
                    .Concat(loadedBroadcasts)
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
