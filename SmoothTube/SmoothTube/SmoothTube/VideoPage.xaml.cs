using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using SmoothTube.Models;
using SmoothTube.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace SmoothTube
{
    public sealed partial class VideoPage : Page
    {
        public VideoItem CurrentVideo { get; set; } = new();

        public List<VideoItem> RecommendedVideos { get; set; } = [];

        public List<CommentItem> Comments { get; set; } = [];

        public List<LiveChatMessageItem> LiveChatMessages { get; set; } = [];

        public string CommentsStatus { get; set; } = "Comments load for YouTube videos when an API key is saved.";

        public string LiveChatStatus { get; set; } = "Live chat appears for active livestreams when YouTube exposes a live chat.";

        private List<VideoItem> AllVideos { get; set; } = [];

        private bool playerHostMapped;
        private bool isPlayerFullScreen;
        private bool playerEventsAttached;
        private bool descriptionExpanded;

        public VideoPage()
        {
            InitializeComponent();

            PlayerWebView.NavigationCompleted += PlayerWebView_NavigationCompleted;
            Unloaded += VideoPage_Unloaded;

            Loaded += VideoPage_Loaded;
            SizeChanged += VideoPage_SizeChanged;
        }

        protected override void OnNavigatedTo(
            NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is VideoNavigationData data)
            {
                CurrentVideo = data.CurrentVideo;

                AllVideos = data.AllVideos;

                RecommendedVideos =
                    AllVideos
                        .Where(v => v != CurrentVideo)
                        .ToList();

                DataContext = CurrentVideo;
                ApplyLiveChatLayout();
                UpdateChannelButtonVisibility();
                _ = UpdateSubscriptionButtonAsync();
                _ = UpdateRatingStateAsync();
                WatchHistoryService.RecordStarted(CurrentVideo);
                _ = EnrichCurrentVideoAsync();
                _ = UpdatePlayerSourceAsync();
                _ = LoadSocialDataAsync();
            }
            else
            {
                AllVideos = VideoCatalog.GetAll();
                CurrentVideo = AllVideos.FirstOrDefault() ?? new VideoItem();
                RecommendedVideos = AllVideos.Skip(1).ToList();
                DataContext = CurrentVideo;
                ApplyLiveChatLayout();
                UpdateChannelButtonVisibility();
                _ = UpdateSubscriptionButtonAsync();
                _ = UpdateRatingStateAsync();
                WatchHistoryService.RecordStarted(CurrentVideo);
                _ = EnrichCurrentVideoAsync();
                _ = UpdatePlayerSourceAsync();
                _ = LoadSocialDataAsync();
            }
        }

        private void UpNextVideo_Tapped(
            object sender,
            TappedRoutedEventArgs e)
        {
            if (sender is Border border &&
                border.DataContext is VideoItem video)
            {
                NavigateToVideo(video);
            }
        }

        private void BackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            StopPlayback();

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async void ChannelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CurrentVideo.ChannelId) &&
                !await TryResolveCurrentChannelAsync())
            {
                return;
            }

            StopPlayback();

            Frame.Navigate(
                typeof(ChannelPage),
                new ChannelItem
                {
                    Id = CurrentVideo.ChannelId,
                    Title = CurrentVideo.Channel,
                    Thumbnail = ""
                });
        }

        private void UpdateChannelButtonVisibility()
        {
            if (ChannelButton == null)
                return;

            ChannelButton.IsEnabled =
                !string.IsNullOrWhiteSpace(CurrentVideo.Channel) ||
                !string.IsNullOrWhiteSpace(CurrentVideo.ChannelId);

            if (LikeButton != null &&
                string.IsNullOrWhiteSpace(CurrentVideo.Likes))
            {
                ToolTipService.SetToolTip(
                    LikeButton,
                    "Rate this video as liked on your signed-in YouTube account");
            }
            else if (LikeButton != null)
            {
                ToolTipService.SetToolTip(
                    LikeButton,
                    $"{CurrentVideo.Likes}. Rate this video as liked on your signed-in YouTube account");
            }

            if (DescriptionPanel != null)
            {
                DescriptionPanel.Visibility =
                    string.IsNullOrWhiteSpace(CurrentVideo.Description)
                        ? Visibility.Collapsed
                        : Visibility.Visible;
            }
        }

        protected override void OnNavigatingFrom(
            NavigatingCancelEventArgs e)
        {
            StopPlayback();
            base.OnNavigatingFrom(e);
        }

        private void VideoPage_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            StopPlayback();
        }

        private void VideoPage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (XamlRoot != null)
            {
                XamlRoot.Changed += XamlRoot_Changed;

                UpdateLayoutForWidth();
            }
        }

        private void XamlRoot_Changed(
            XamlRoot sender,
            XamlRootChangedEventArgs args)
        {
            UpdateLayoutForWidth();
        }

        private void VideoPage_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            UpdatePlayerSize();
        }

        private void UpdateLayoutForWidth()
        {
            if (isPlayerFullScreen)
                return;

            double width = XamlRoot.Size.Width;

            if (width < 1300)
            {
                Grid.SetColumn(UpNextPanel, 0);
                Grid.SetRow(UpNextPanel, 1);

                UpNextPanel.Width =
                    double.NaN;

                UpNextHeaderGrid.Width =
                    double.NaN;

                LiveChatHeaderGrid.Width =
                    double.NaN;

                UpNextPanel.Margin =
                    new Thickness(0, 24, 0, 0);

                SidebarColumn.Width =
                    new GridLength(0);
            }
            else
            {
                Grid.SetColumn(UpNextPanel, 1);
                Grid.SetRow(UpNextPanel, 0);

                UpNextPanel.Margin =
                    new Thickness(0);

                SidebarColumn.Width =
                    new GridLength(340);

                UpNextPanel.Width =
                    SidebarColumn.Width.Value;

                UpNextHeaderGrid.Width =
                    SidebarColumn.Width.Value;

                LiveChatHeaderGrid.Width =
                    SidebarColumn.Width.Value;
            }

            UpdatePlayerSize();
        }

        private void ApplyLiveChatLayout()
        {
            if (LiveChatPanel == null ||
                MainContentPanel == null)
            {
                return;
            }

            LiveChatPanel.Visibility =
                CurrentVideo.IsLive
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            UpdateLiveChatStatusVisibility();
        }

        private void UpdatePlayerSize()
        {
            if (PlayerBorder == null)
                return;

            if (isPlayerFullScreen && XamlRoot != null)
            {
                PlayerBorder.Width = XamlRoot.Size.Width;
                PlayerBorder.Height = XamlRoot.Size.Height;
                PlayerWebView.Width = XamlRoot.Size.Width;
                PlayerWebView.Height = XamlRoot.Size.Height;
                return;
            }

            double availableWidth =
                MainLayoutGrid.ActualWidth;

            if (availableWidth <= 0)
                return;

            double playerWidth =
                SidebarColumn.Width.Value > 0
                    ? availableWidth - 364
                    : availableWidth;

            PlayerBorder.Height =
                playerWidth * 9 / 16;

            PlayerWebView.Width = double.NaN;
            PlayerWebView.Height = double.NaN;
        }

        private async Task UpdatePlayerSourceAsync()
        {
            bool canEmbedYouTube =
                CurrentVideo.Category == "YouTube" &&
                !string.IsNullOrWhiteSpace(CurrentVideo.Id) &&
                CurrentVideo.IsEmbeddable;

            bool canOpenYouTube =
                CurrentVideo.Category == "YouTube" &&
                !string.IsNullOrWhiteSpace(CurrentVideo.Id);

            OpenOnYouTubeButton.Visibility =
                canOpenYouTube
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            PlayerWebView.Visibility =
                canEmbedYouTube
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            BlockedPlayerOverlay.Visibility =
                canOpenYouTube && !canEmbedYouTube
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            MockPlayerGrid.Visibility =
                canEmbedYouTube || canOpenYouTube
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            MockControlsPanel.Visibility =
                canEmbedYouTube
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            PlayerBorder.CornerRadius =
                canEmbedYouTube
                    ? new CornerRadius(16)
                    : new CornerRadius(16, 16, 0, 0);

            if (canEmbedYouTube)
            {
                string videoId =
                    Uri.EscapeDataString(CurrentVideo.Id);

                await PlayerWebView.EnsureCoreWebView2Async();
                EnsurePlayerHostMapped();
                EnsurePlayerEventsAttached();

                PlayerWebView.CoreWebView2.ContainsFullScreenElementChanged -=
                    CoreWebView2_ContainsFullScreenElementChanged;

                PlayerWebView.CoreWebView2.ContainsFullScreenElementChanged +=
                    CoreWebView2_ContainsFullScreenElementChanged;

                PlayerWebView.CoreWebView2.Navigate(
                    $"https://smoothtube.local/youtube-player.html?videoId={videoId}");
            }
        }

        private void OpenOnYouTube_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CurrentVideo.Id))
                return;

            var window =
                new YouTubeWatchWindow(CurrentVideo.Id);

            window.Activate();
        }

        private void EnsurePlayerHostMapped()
        {
            if (playerHostMapped)
                return;

            string assetsPath =
                Path.Combine(AppContext.BaseDirectory, "Assets");

            PlayerWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "smoothtube.local",
                assetsPath,
                CoreWebView2HostResourceAccessKind.Allow);

            playerHostMapped = true;
        }

        private void EnsurePlayerEventsAttached()
        {
            if (playerEventsAttached ||
                PlayerWebView.CoreWebView2 == null)
            {
                return;
            }

            PlayerWebView.CoreWebView2.NewWindowRequested +=
                CoreWebView2_NewWindowRequested;

            PlayerWebView.CoreWebView2.NavigationStarting +=
                CoreWebView2_NavigationStarting;

            PlayerWebView.CoreWebView2.WebMessageReceived +=
                CoreWebView2_WebMessageReceived;

            playerEventsAttached = true;
        }

        private void CoreWebView2_WebMessageReceived(
            CoreWebView2 sender,
            CoreWebView2WebMessageReceivedEventArgs args)
        {
            string message = args.TryGetWebMessageAsString();

            if (message == "ended" &&
                AutoPlayUpNextSwitch.IsOn &&
                RecommendedVideos.Count > 0)
            {
                NavigateToVideo(RecommendedVideos[0]);
            }
        }

        private void CoreWebView2_NewWindowRequested(
            CoreWebView2 sender,
            CoreWebView2NewWindowRequestedEventArgs args)
        {
            if (TryHandlePlayerUri(args.Uri))
            {
                args.Handled = true;
            }
        }

        private void CoreWebView2_NavigationStarting(
            CoreWebView2 sender,
            CoreWebView2NavigationStartingEventArgs args)
        {
            if (args.Uri.StartsWith(
                    "https://smoothtube.local/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (TryHandlePlayerUri(args.Uri))
            {
                args.Cancel = true;
            }
        }

        private bool TryHandlePlayerUri(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsedUri))
                return false;

            string videoId =
                ExtractVideoId(parsedUri);

            if (!string.IsNullOrWhiteSpace(videoId) &&
                videoId != CurrentVideo.Id)
            {
                StopPlayback();

                VideoItem nextVideo =
                    RecommendedVideos.FirstOrDefault(video => video.Id == videoId) ??
                    new VideoItem
                    {
                        Id = videoId,
                        Title = "Loading...",
                        Category = "YouTube",
                        IsEmbeddable = true
                    };

                NavigateToVideo(nextVideo);

                return true;
            }

            string channelId =
                ExtractChannelId(parsedUri);

            if (!string.IsNullOrWhiteSpace(channelId))
            {
                StopPlayback();

                Frame.Navigate(
                    typeof(ChannelPage),
                    new ChannelItem
                    {
                        Id = channelId,
                        Title = ""
                    });

                return true;
            }

            return parsedUri.Host.Contains(
                "youtube.com",
                StringComparison.OrdinalIgnoreCase);
        }

        private void CoreWebView2_ContainsFullScreenElementChanged(
            CoreWebView2 sender,
            object args)
        {
            ApplyPlayerFullScreen(sender.ContainsFullScreenElement);
        }

        private void ApplyPlayerFullScreen(bool isFullScreen)
        {
            isPlayerFullScreen = isFullScreen;
            MainWindow.Instance?.SetFullScreen(isFullScreen);

            VideoActionsPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            UpNextPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            VideoInfoPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            DescriptionPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            CommentsPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            LiveChatPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            PageScrollViewer.VerticalScrollBarVisibility =
                isFullScreen
                    ? ScrollBarVisibility.Disabled
                    : ScrollBarVisibility.Auto;

            PageScrollViewer.HorizontalScrollBarVisibility =
                isFullScreen
                    ? ScrollBarVisibility.Disabled
                    : ScrollBarVisibility.Disabled;

            MainLayoutGrid.Padding =
                isFullScreen
                    ? new Thickness(0)
                    : new Thickness(40);

            MainLayoutGrid.ColumnSpacing =
                isFullScreen
                    ? 0
                    : 24;

            MainContentPanel.Spacing =
                isFullScreen
                    ? 0
                    : 24;

            SidebarColumn.Width =
                isFullScreen
                    ? new GridLength(0)
                    : new GridLength(340);

            PlayerBorder.CornerRadius =
                isFullScreen
                    ? new CornerRadius(0)
                    : new CornerRadius(16);

            if (isFullScreen && XamlRoot != null)
            {
                Grid.SetColumn(MainContentPanel, 0);
                Grid.SetColumnSpan(MainContentPanel, 2);
                Grid.SetRowSpan(MainContentPanel, 2);
                MainContentPanel.Width = XamlRoot.Size.Width;
                MainContentPanel.Height = XamlRoot.Size.Height;
                MainLayoutGrid.Width = XamlRoot.Size.Width;
                MainLayoutGrid.Height = XamlRoot.Size.Height;
                PageScrollViewer.Width = XamlRoot.Size.Width;
                PageScrollViewer.Height = XamlRoot.Size.Height;
                PlayerBorder.Width = XamlRoot.Size.Width;
                PlayerBorder.Height = XamlRoot.Size.Height;
                PlayerWebView.Width = XamlRoot.Size.Width;
                PlayerWebView.Height = XamlRoot.Size.Height;
            }
            else
            {
                Grid.SetColumnSpan(MainContentPanel, 1);
                Grid.SetRowSpan(MainContentPanel, 1);
                MainContentPanel.Width = double.NaN;
                MainContentPanel.Height = double.NaN;
                MainLayoutGrid.Width = double.NaN;
                MainLayoutGrid.Height = double.NaN;
                PageScrollViewer.Width = double.NaN;
                PageScrollViewer.Height = double.NaN;
                PlayerBorder.Width = double.NaN;
                PlayerWebView.Width = double.NaN;
                PlayerWebView.Height = double.NaN;
                UpdateLayoutForWidth();
            }
        }

        private void StopPlayback()
        {
            try
            {
                if (PlayerWebView.CoreWebView2 != null)
                {
                    _ = PlayerWebView.ExecuteScriptAsync(
                        "document.querySelector('video')?.pause();");

                    PlayerWebView.CoreWebView2.Navigate("about:blank");
                }
                else
                {
                    PlayerWebView.Source = new Uri("about:blank");
                }
            }
            catch (InvalidOperationException)
            {
            }

            ApplyPlayerFullScreen(false);
        }

        private async void PlayerWebView_NavigationCompleted(
            WebView2 sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            await Task.CompletedTask;
        }

        private async void RefreshLiveChat_Click(
            object sender,
            RoutedEventArgs e)
        {
            await LoadLiveChatAsync();
        }

        private void DescriptionToggleButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            descriptionExpanded = !descriptionExpanded;

            DescriptionTextBlock.MaxLines =
                descriptionExpanded
                    ? 0
                    : 3;

            DescriptionToggleButton.Content =
                descriptionExpanded
                    ? "Show less"
                    : "Show more";
        }

        private async void LikeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            bool success =
                await ServiceLocator.YouTube.RateVideoAsync(
                    CurrentVideo.Id,
                    "like");

            ToolTipService.SetToolTip(
                LikeButton,
                success ? "Liked" : "Sign in again to like this video");

            if (success)
            {
                ApplyRatingState("like");
            }
        }

        private async void DislikeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            bool success =
                await ServiceLocator.YouTube.RateVideoAsync(
                    CurrentVideo.Id,
                    "dislike");

            ToolTipService.SetToolTip(
                DislikeButton,
                success ? "Disliked" : "Sign in again to dislike this video");

            if (success)
            {
                ApplyRatingState("dislike");
            }
        }

        private async Task UpdateRatingStateAsync()
        {
            if (LikeButton == null ||
                DislikeButton == null ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                return;
            }

            string rating =
                await ServiceLocator.YouTube.GetVideoRatingAsync(
                    CurrentVideo.Id);

            ApplyRatingState(rating);
        }

        private void ApplyRatingState(string rating)
        {
            Brush accentBrush =
                Application.Current.Resources.TryGetValue(
                    "AccentButtonBackground",
                    out object accentResource) &&
                accentResource is Brush brush
                    ? brush
                    : new SolidColorBrush(
                        ColorHelper.FromArgb(255, 210, 140, 230));

            LikeButton.Background =
                rating == "like"
                    ? accentBrush
                    : null;

            DislikeButton.Background =
                rating == "dislike"
                    ? accentBrush
                    : null;

            LikeButton.Opacity =
                rating == "dislike"
                    ? 0.65
                    : 1;

            DislikeButton.Opacity =
                rating == "like"
                    ? 0.65
                    : 1;
        }

        private async void SubscribeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SubscribeButton.IsEnabled = false;
            SubscribeButton.Content = "Subscribing...";

            if (string.IsNullOrWhiteSpace(CurrentVideo.ChannelId) &&
                !await TryResolveCurrentChannelAsync())
            {
                SubscribeButton.Content = "Channel unavailable";
                SubscribeButton.IsEnabled = true;
                return;
            }

            bool success =
                await ServiceLocator.YouTube.SubscribeToChannelAsync(
                    CurrentVideo.ChannelId);

            SubscribeButton.Content =
                success
                    ? "Subscribed"
                    : "Sign in to subscribe";

            SubscribeButton.IsEnabled = !success;
        }

        private async Task UpdateSubscriptionButtonAsync()
        {
            if (SubscribeButton == null ||
                string.IsNullOrWhiteSpace(CurrentVideo.ChannelId))
            {
                return;
            }

            bool isSubscribed =
                await ServiceLocator.YouTube.IsSubscribedToChannelAsync(
                    CurrentVideo.ChannelId);

            SubscribeButton.Content =
                isSubscribed
                    ? "Subscribed"
                    : "Subscribe";

            SubscribeButton.IsEnabled = !isSubscribed;
        }

        private void VideoThumbnail_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Image image ||
                image.DataContext is not VideoItem video ||
                !Uri.TryCreate(video.Thumbnail, UriKind.Absolute, out Uri? uri))
            {
                return;
            }

            image.Source = new BitmapImage(uri);
        }

        private async Task EnrichCurrentVideoAsync()
        {
            if (CurrentVideo.Category != "YouTube" ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id) ||
                !NeedsVideoDetails(CurrentVideo))
            {
                return;
            }

            VideoItem? enrichedVideo =
                await ServiceLocator.YouTube.GetVideoAsync(CurrentVideo.Id);

            if (enrichedVideo == null)
                return;

            CurrentVideo.Title = enrichedVideo.Title;
            CurrentVideo.Channel = enrichedVideo.Channel;
            CurrentVideo.ChannelId = enrichedVideo.ChannelId;
            CurrentVideo.Views = enrichedVideo.Views;
            CurrentVideo.Likes = enrichedVideo.Likes;
            CurrentVideo.Duration = enrichedVideo.Duration;
            CurrentVideo.PublishedAt = enrichedVideo.PublishedAt;
            CurrentVideo.Thumbnail = enrichedVideo.Thumbnail;
            CurrentVideo.Description = enrichedVideo.Description;
            CurrentVideo.IsLive = enrichedVideo.IsLive;
            CurrentVideo.IsPremiere = enrichedVideo.IsPremiere;
            CurrentVideo.LiveChatId = enrichedVideo.LiveChatId;
            CurrentVideo.IsEmbeddable = enrichedVideo.IsEmbeddable;

            DataContext = null;
            DataContext = CurrentVideo;
            ApplyLiveChatLayout();
            UpdateChannelButtonVisibility();
            _ = UpdateSubscriptionButtonAsync();
            Bindings.Update();

            if (CurrentVideo.IsLive)
            {
                await LoadLiveChatAsync();
            }
        }

        private static bool NeedsVideoDetails(VideoItem video)
        {
            return string.IsNullOrWhiteSpace(video.Title) ||
                video.Title == "YouTube video" ||
                video.Title == "Loading..." ||
                string.IsNullOrWhiteSpace(video.Channel) ||
                string.IsNullOrWhiteSpace(video.Description) ||
                string.IsNullOrWhiteSpace(video.Duration) ||
                video.IsLive && string.IsNullOrWhiteSpace(video.LiveChatId);
        }

        private void NavigateToVideo(VideoItem video)
        {
            StopPlayback();

            Frame.Navigate(
                typeof(VideoPage),
                new VideoNavigationData
                {
                    CurrentVideo = video,
                    AllVideos = AllVideos.Count > 0
                        ? AllVideos
                        : [video]
                });
        }

        private async Task<bool> TryResolveCurrentChannelAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentVideo.Channel))
                return false;

            List<SearchResultItem> results =
                await ServiceLocator.YouTube.SearchAllAsync(CurrentVideo.Channel);

            ChannelItem? channel =
                results
                    .Where(result => result.Kind == "Channel")
                    .Select(result => result.Channel)
                    .FirstOrDefault(item =>
                        item != null &&
                        item.Title.Equals(
                            CurrentVideo.Channel,
                            StringComparison.OrdinalIgnoreCase));

            channel ??=
                results
                    .Where(result => result.Kind == "Channel")
                    .Select(result => result.Channel)
                    .FirstOrDefault(item => item != null);

            if (channel == null ||
                string.IsNullOrWhiteSpace(channel.Id))
            {
                return false;
            }

            CurrentVideo.ChannelId = channel.Id;
            return true;
        }

        private static string ExtractVideoId(Uri uri)
        {
            string path =
                uri.AbsolutePath.Trim('/');

            if (path.StartsWith("embed/", StringComparison.OrdinalIgnoreCase))
                return path["embed/".Length..].Split('/')[0];

            if (path.StartsWith("shorts/", StringComparison.OrdinalIgnoreCase))
                return path["shorts/".Length..].Split('/')[0];

            if (path.StartsWith("live/", StringComparison.OrdinalIgnoreCase))
                return path["live/".Length..].Split('/')[0];

            string query =
                uri.Query.TrimStart('?');

            foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair =
                    part.Split('=', 2);

                if (pair.Length == 2 &&
                    pair[0] == "v")
                {
                    return Uri.UnescapeDataString(pair[1]);
                }
            }

            return "";
        }

        private static string ExtractChannelId(Uri uri)
        {
            string path =
                uri.AbsolutePath.Trim('/');

            if (path.StartsWith("channel/", StringComparison.OrdinalIgnoreCase))
                return path["channel/".Length..].Split('/')[0];

            return "";
        }

        private async Task LoadSocialDataAsync()
        {
            Comments = [];
            LiveChatMessages = [];

            CommentsStatus =
                CurrentVideo.Category == "YouTube"
                    ? "Loading comments..."
                    : "Comments are available for YouTube API videos.";

            LiveChatStatus =
                CurrentVideo.IsLive
                    ? "Loading live chat..."
                    : "This video is not an active livestream.";

            Bindings.Update();
            UpdateLiveChatStatusVisibility();

            await LoadCommentsAsync();
            await LoadLiveChatAsync();
        }

        private async Task LoadCommentsAsync()
        {
            if (CurrentVideo.Category != "YouTube" ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                CommentsStatus = "Comments are available for YouTube API videos.";
                Bindings.Update();
                return;
            }

            Comments =
                await ServiceLocator.YouTube.GetCommentsAsync(CurrentVideo.Id);

            CommentsStatus =
                Comments.Count == 0
                    ? "No comments loaded. Comments may be disabled, unavailable, or the API key may be missing."
                    : $"{Comments.Count} comments";

            Bindings.Update();
        }

        private async Task LoadLiveChatAsync()
        {
            if (!CurrentVideo.IsLive)
            {
                HideLiveChatEmbed();
                LiveChatStatus = "This video is not an active livestream.";
                Bindings.Update();
                UpdateLiveChatStatusVisibility();
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentVideo.LiveChatId))
            {
                LiveChatStatus = "Loading YouTube live chat...";
                Bindings.Update();
                UpdateLiveChatStatusVisibility();
                await ShowLiveChatEmbedAsync();
                return;
            }

            HideLiveChatEmbed();

            LiveChatMessages =
                await ServiceLocator.YouTube.GetLiveChatMessagesAsync(CurrentVideo.LiveChatId);

            LiveChatStatus =
                LiveChatMessages.Count == 0
                    ? "No live chat messages loaded. Chat may be unavailable, ended, or the API key may be missing."
                    : $"{LiveChatMessages.Count} live chat messages";

            Bindings.Update();
            UpdateLiveChatStatusVisibility();
        }

        private async Task ShowLiveChatEmbedAsync()
        {
            if (LiveChatWebView == null ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                LiveChatStatus = "This livestream does not expose an active live chat.";
                Bindings.Update();
                return;
            }

            try
            {
                LiveChatMessagesScrollViewer.Visibility = Visibility.Collapsed;
                LiveChatWebViewFrame.Visibility = Visibility.Visible;

                await LiveChatWebView.EnsureCoreWebView2Async();

                string videoId =
                    Uri.EscapeDataString(CurrentVideo.Id);

                LiveChatWebView.CoreWebView2.Navigate(
                    $"https://www.youtube.com/live_chat?v={videoId}&embed_domain=smoothtube.local");

                LiveChatStatus = "";
                Bindings.Update();
                UpdateLiveChatStatusVisibility();
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                ex is COMException)
            {
                HideLiveChatEmbed();
                LiveChatStatus = "Live chat could not be opened for this stream.";
                Bindings.Update();
                UpdateLiveChatStatusVisibility();
            }
        }

        private void HideLiveChatEmbed()
        {
            if (LiveChatWebView != null)
            {
                LiveChatWebViewFrame.Visibility = Visibility.Collapsed;
            }

            if (LiveChatMessagesScrollViewer != null)
            {
                LiveChatMessagesScrollViewer.Visibility = Visibility.Visible;
            }
        }

        private void UpdateLiveChatStatusVisibility()
        {
            if (LiveChatStatusTextBlock == null)
                return;

            LiveChatStatusTextBlock.Visibility =
                string.IsNullOrWhiteSpace(LiveChatStatus)
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            if (SidebarLiveChatStatusTextBlock != null)
            {
                SidebarLiveChatStatusTextBlock.Visibility =
                    LiveChatStatusTextBlock.Visibility;
            }
        }
    }
}
