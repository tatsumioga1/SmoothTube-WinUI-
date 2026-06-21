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
using System.Linq;

namespace SmoothTube
{
    public sealed partial class ChannelPage : Page
    {
        public ChannelItem Channel { get; set; } = new();

        public ObservableCollection<VideoItem> UploadVideos { get; } = [];

        public ObservableCollection<VideoItem> ShortVideos { get; } = [];

        public ObservableCollection<VideoItem> LivestreamVideos { get; } = [];

        private List<VideoItem> allVideos = [];

        public string StatusText { get; set; } = "";

        private int requestedUploadCount = 24;
        private bool isLoadingMore;

        protected override void OnNavigatedTo(
            NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is ChannelItem channel)
            {
                Channel = channel;
                requestedUploadCount = 24;
                Bindings.Update();
                UpdateBannerVisibility();
                _ = LoadChannelAsync();
            }
        }

        public ChannelPage()
        {
            InitializeComponent();
        }

        private async System.Threading.Tasks.Task LoadChannelAsync()
        {
            StatusText = "Loading channel...";
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

                await UpdateSubscriptionButtonAsync();

                List<VideoItem> videos =
                    await ServiceLocator.YouTube.GetChannelVideosAsync(
                        Channel.Id,
                        requestedUploadCount);

                ReplaceVideos(videos);
            }
            catch (System.Exception ex)
            {
                StatusText = $"Could not load this channel: {ex.Message}";
                Bindings.Update();
                return;
            }

            StatusText =
                allVideos.Count == 0
                    ? "No recent uploads loaded for this channel."
                    : $"{UploadVideos.Count} uploads, {ShortVideos.Count} shorts, {LivestreamVideos.Count} livestreams";

            Bindings.Update();
        }

        private async void LoadMoreButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoadingMore)
                return;

            isLoadingMore = true;
            requestedUploadCount += 24;
            StatusText = "Loading more uploads...";
            Bindings.Update();

            int previousCount = allVideos.Count;
            await LoadChannelAsync();

            if (allVideos.Count <= previousCount)
            {
                StatusText =
                    "No more uploads were returned for this channel.";

                Bindings.Update();
            }

            isLoadingMore = false;
        }

        private void ReplaceVideos(IEnumerable<VideoItem> videos)
        {
            allVideos =
                videos
                    .Where(video => !string.IsNullOrWhiteSpace(video.Id))
                    .GroupBy(video => video.Id)
                    .Select(group => group.First())
                    .ToList();

            UploadVideos.Clear();
            ShortVideos.Clear();
            LivestreamVideos.Clear();

            foreach (VideoItem video in allVideos.Where(video =>
                !video.IsLive &&
                !IsLikelyShort(video)))
            {
                UploadVideos.Add(video);
            }

            foreach (VideoItem video in allVideos.Where(video =>
                !video.IsLive &&
                IsLikelyShort(video)))
            {
                ShortVideos.Add(video);
            }

            foreach (VideoItem video in allVideos.Where(video => video.IsLive))
            {
                LivestreamVideos.Add(video);
            }
        }

        private static bool IsLikelyShort(VideoItem video)
        {
            if (video.IsShort)
                return true;

            string title = video.Title ?? "";

            return
                title.Contains("#short", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" shorts", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" short ", StringComparison.OrdinalIgnoreCase) ||
                title.EndsWith(" short", StringComparison.OrdinalIgnoreCase);
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

        private async System.Threading.Tasks.Task UpdateSubscriptionButtonAsync()
        {
            bool isSubscribed =
                await ServiceLocator.YouTube.IsSubscribedToChannelAsync(
                    Channel.Id);

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
