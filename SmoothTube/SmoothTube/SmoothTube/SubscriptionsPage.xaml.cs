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
        public ObservableCollection<VideoItem> Videos { get; } = [];

        public ObservableCollection<VideoItem> PremiereVideos { get; } = [];

        public ObservableCollection<VideoItem> LivestreamVideos { get; } = [];

        private readonly List<VideoItem> loadedUploads = [];

        private readonly List<VideoItem> loadedBroadcasts = [];

        public string StatusText { get; set; } =
            "Sign in from Settings to load your YouTube subscriptions.";

        public Visibility LoadMoreVisibility { get; set; } = Visibility.Visible;

        private bool isLoaded;
        private bool isLoading;
        private bool broadcastsLoaded;
        private bool broadcastsLoading;
        private int loadedUploadDays = 1;
        private CancellationTokenSource? loadCancellation;

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
            await LoadVideosAsync(false);
        }

        private void Filters_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (!isLoaded)
                return;

            if (ReferenceEquals(sender, IncludeShortsSwitch))
            {
                ApplyVisibleFilters();
            }
        }

        private async void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await LoadVideosAsync(true);
        }

        private async void LoadMoreButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoading)
                return;

            loadedUploadDays++;

            await LoadUploadRangeAsync(
                loadedUploadDays,
                true,
                10,
                loadCancellation?.Token ?? CancellationToken.None);
        }

        private async Task LoadVideosAsync(bool forceRefresh)
        {
            loadCancellation?.Cancel();
            loadCancellation = new CancellationTokenSource();

            CancellationToken cancellationToken =
                loadCancellation.Token;

            if (forceRefresh)
            {
                ServiceLocator.YouTube.ClearSubscribedVideoCache();
            }

            loadedUploadDays = 1;
            loadedUploads.Clear();
            loadedBroadcasts.Clear();

            broadcastsLoaded = false;
            broadcastsLoading = false;

            Videos.Clear();
            PremiereVideos.Clear();
            LivestreamVideos.Clear();

            StatusText = "Loading subscriptions...";
            Bindings.Update();

            try
            {
                Task uploadsTask =
                    LoadUploadRangeAsync(
                        loadedUploadDays,
                        false,
                        null,
                        cancellationToken);

                Task broadcastsTask =
                    LoadBroadcastsAsync(cancellationToken);

                await Task.WhenAll(uploadsTask, broadcastsTask);

                ApplyVisibleFilters();
            }
            catch (TaskCanceledException)
            {
            }
        }

        private async Task LoadUploadRangeAsync(
            int days,
            bool append,
            int? maxNewVideos,
            CancellationToken cancellationToken)
        {
            if (isLoading)
                return;

            isLoading = true;
            LoadMoreButton.IsEnabled = false;

            int startingCount = loadedUploads.Count;
            int addedCount = 0;

            StatusText = append
                ? $"Loading uploads from day {days}..."
                : "Loading latest subscription uploads...";

            Bindings.Update();

            try
            {
                await foreach (List<VideoItem> batch in
                    ServiceLocator.YouTube.GetSubscribedVideoBatchesAsync(
                        days,
                        true,
                        cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    List<VideoItem> uploadVideos =
                        batch
                            .Where(video => !video.IsLive && !video.IsPremiere)
                            .OrderByDescending(GetPublishedAtSort)
                            .ToList();

                    List<VideoItem> broadcastVideos =
                        batch
                            .Where(video => video.IsLive || video.IsPremiere)
                            .OrderByDescending(GetPublishedAtSort)
                            .ToList();

                    if (append)
                    {
                        uploadVideos =
                            uploadVideos
                                .Where(video =>
                                    loadedUploads.All(existingVideo =>
                                        existingVideo.Id != video.Id))
                                .Take((maxNewVideos ?? int.MaxValue) - addedCount)
                                .ToList();
                    }

                    int beforeMergeCount =
                        loadedUploads.Count;

                    MergeVideos(loadedUploads, uploadVideos);
                    MergeVideos(loadedBroadcasts, broadcastVideos);

                    addedCount +=
                        loadedUploads.Count - beforeMergeCount;

                    ApplyVisibleFilters(true);

                    if (append &&
                        maxNewVideos != null &&
                        addedCount >= maxNewVideos.Value)
                    {
                        break;
                    }
                }

                if (append && loadedUploads.Count == startingCount)
                {
                    StatusText = $"No more uploads found for day {days}.";
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

            broadcastsLoading = true;

            StatusText = "Checking live and upcoming subscriptions...";
            Bindings.Update();

            try
            {
                await foreach (List<VideoItem> batch in
                    ServiceLocator.YouTube.GetSubscribedBroadcastBatchesAsync(
                        cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    MergeVideos(loadedBroadcasts, batch);

                    ApplyVisibleFilters(true);
                }

                broadcastsLoaded = true;
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
                    "Live and premiere checks failed. Try Refresh in a moment.";

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
                    .Where(video => IncludeShortsSwitch.IsOn || !IsLikelyShort(video))
                    .ToList();

            Videos.Clear();
            PremiereVideos.Clear();
            LivestreamVideos.Clear();

            foreach (VideoItem video in visibleVideos
                .Where(video => !video.IsLive && !video.IsPremiere)
                .OrderByDescending(GetPublishedAtSort))
            {
                Videos.Add(video);
            }

            foreach (VideoItem video in visibleVideos
                .Where(video => video.IsPremiere)
                .OrderByDescending(GetPublishedAtSort))
            {
                PremiereVideos.Add(video);
            }

            foreach (VideoItem video in visibleVideos
                .Where(video => video.IsLive)
                .OrderByDescending(GetPublishedAtSort))
            {
                LivestreamVideos.Add(video);
            }

            StatusText =
                Videos.Count == 0 && PremiereVideos.Count == 0 && LivestreamVideos.Count == 0
                    ? stillLoading
                        ? "Loading subscriptions..."
                        : "No recent videos loaded for these filters."
                    : FormatStatusText(stillLoading);

            Bindings.Update();
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
                int totalSeconds =
                    minutes * 60 + seconds;

                return titleLooksShort ||
                    totalSeconds <= 180;
            }

            return titleLooksShort;
        }

        private string FormatStatusText(bool isLoading)
        {
            string suffix =
                isLoading
                    ? "..."
                    : "";

            return
                $"{Videos.Count} videos, {PremiereVideos.Count} premieres, {LivestreamVideos.Count} livestreams{suffix}";
        }

        private static void MergeVideos(
            List<VideoItem> target,
            IEnumerable<VideoItem> videos)
        {
            foreach (VideoItem video in videos)
            {
                if (!target.Any(existingVideo => existingVideo.Id == video.Id))
                {
                    target.Add(video);
                }
            }
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