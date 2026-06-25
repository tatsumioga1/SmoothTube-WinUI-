using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using SmoothTube.Models;
using SmoothTube.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace SmoothTube
{
    public sealed partial class VideoPage : Page
    {
        private static readonly HttpClient MetadataHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public VideoItem CurrentVideo { get; set; } = new();

        public string CurrentChannelThumbnail { get; set; } = "";

        public string CurrentChannelSubscriberText { get; set; } = "";

        public string CurrentVideoMetaText { get; set; } = "";

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
        private bool descriptionRichTextBuilt;
        private DispatcherTimer? progressTimer;
        private bool isStoppingPlayback;
        private bool playerLoadedForLiveMode;
        private bool liveChatSignInButtonDismissed;
        private DateTimeOffset? playbackStartedAt;
        private double playbackStartedResumeSeconds;

        public VideoPage()
        {
            InitializeComponent();

            AddHandler(
                UIElement.KeyDownEvent,
                new KeyEventHandler(VideoPage_KeyDown),
                true);

            AddHandler(
                UIElement.KeyUpEvent,
                new KeyEventHandler(VideoPage_KeyUp),
                true);

            PlayerWebView.NavigationCompleted += PlayerWebView_NavigationCompleted;
            LiveChatWebView.NavigationCompleted += LiveChatWebView_NavigationCompleted;
            Unloaded += VideoPage_Unloaded;

            Loaded += VideoPage_Loaded;
            SizeChanged += VideoPage_SizeChanged;
        }

        private async void VideoPage_KeyDown(
            object sender,
            KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Space)
            {
                return;
            }

            if (IsTextInputFocused())
            {
                return;
            }

            // Mark Space as handled before focused buttons/cards can consume it.
            // The matching KeyUp handler below prevents Button from firing Click.
            e.Handled = true;
            await TogglePlayerPlaybackAsync();
        }

        private void VideoPage_KeyUp(
            object sender,
            KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Space)
            {
                return;
            }

            if (IsTextInputFocused())
            {
                return;
            }

            // Button controls usually activate on Space key-up.
            // Handle this so Space never accidentally clicks Back/Like/Subscribe/etc.
            e.Handled = true;
        }

        private static bool IsTextInputFocused()
        {
            object? focusedElement =
                FocusManager.GetFocusedElement();

            return focusedElement is TextBox ||
                focusedElement is PasswordBox ||
                focusedElement is RichEditBox ||
                focusedElement is AutoSuggestBox;
        }

        private async Task TogglePlayerPlaybackAsync()
        {
            try
            {
                if (PlayerWebView?.CoreWebView2 == null)
                {
                    return;
                }

                await PlayerWebView.CoreWebView2.ExecuteScriptAsync(
                    "window.__smoothTubeTogglePlayback && window.__smoothTubeTogglePlayback();");
            }
            catch (Exception)
            {
                // Ignore player toggle failures so keyboard handling never crashes the page.
            }
        }

        protected override void OnNavigatedTo(
            NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is VideoNavigationData data)
            {
                CurrentVideo = data.CurrentVideo;
                liveChatSignInButtonDismissed = false;
                EnsureCurrentVideoDefaults();
                ResetChannelPresentation();
                UpdateVideoMetaText();

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
                if (LooksLikeLiveVideo(CurrentVideo))
                {
                    CurrentVideo.IsLive = true;
                    CurrentVideo.IsPremiere = false;
                    CurrentVideo.ResumeSeconds = 0;
                    CurrentVideo.Progress = 0;
                    CurrentVideo.DurationSeconds = 0;

                    if (string.IsNullOrWhiteSpace(CurrentVideo.Duration))
                    {
                        CurrentVideo.Duration = "LIVE";
                    }
                }
                else
                {
                    WatchHistoryService.ApplySavedProgress(CurrentVideo);
                }

                WatchHistoryService.RecordStarted(CurrentVideo);
                RefreshCurrentVideoBindings();
                _ = EnrichCurrentVideoAsync();
                _ = LoadChannelPresentationAsync();
                _ = UpdatePlayerSourceAsync();
                _ = LoadSocialDataAsync();
            }
            else
            {
                AllVideos = VideoCatalog.GetAll();
                CurrentVideo = AllVideos.FirstOrDefault() ?? new VideoItem();
                EnsureCurrentVideoDefaults();
                ResetChannelPresentation();
                UpdateVideoMetaText();
                RecommendedVideos = AllVideos.Skip(1).ToList();
                DataContext = CurrentVideo;
                ApplyLiveChatLayout();
                UpdateChannelButtonVisibility();
                _ = UpdateSubscriptionButtonAsync();
                _ = UpdateRatingStateAsync();
                if (LooksLikeLiveVideo(CurrentVideo))
                {
                    CurrentVideo.IsLive = true;
                    CurrentVideo.IsPremiere = false;
                    CurrentVideo.ResumeSeconds = 0;
                    CurrentVideo.Progress = 0;
                    CurrentVideo.DurationSeconds = 0;

                    if (string.IsNullOrWhiteSpace(CurrentVideo.Duration))
                    {
                        CurrentVideo.Duration = "LIVE";
                    }
                }
                else
                {
                    WatchHistoryService.ApplySavedProgress(CurrentVideo);
                }

                WatchHistoryService.RecordStarted(CurrentVideo);
                RefreshCurrentVideoBindings();
                _ = EnrichCurrentVideoAsync();
                _ = LoadChannelPresentationAsync();
                _ = UpdatePlayerSourceAsync();
                _ = LoadSocialDataAsync();
            }
        }

        private static bool LooksLikeLiveVideo(VideoItem video)
        {
            string title = video?.Title ?? "";
            string duration = video?.Duration ?? "";

            return video?.IsLive == true ||
                title.Contains("[ live ]", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("[live]", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("live ", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" live ", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("livestream", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("live stream", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("🔴", StringComparison.OrdinalIgnoreCase) ||
                duration.Equals("LIVE", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureCurrentVideoDefaults()
        {
            if (CurrentVideo == null)
            {
                CurrentVideo = new VideoItem();
            }

            if (LooksLikeLiveVideo(CurrentVideo))
            {
                CurrentVideo.IsLive = true;
                CurrentVideo.IsPremiere = false;

                if (string.IsNullOrWhiteSpace(CurrentVideo.Duration))
                {
                    CurrentVideo.Duration = "LIVE";
                }
            }

            if (string.IsNullOrWhiteSpace(CurrentVideo.Category) &&
                !string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                CurrentVideo.Category = "YouTube";
            }

            if (CurrentVideo.Category == "YouTube" &&
                !string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                CurrentVideo.IsEmbeddable = true;
            }

            if (string.IsNullOrWhiteSpace(CurrentVideo.Title) &&
                !string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                CurrentVideo.Title = "YouTube video";
            }
        }

        private void RefreshCurrentVideoBindings()
        {
            UpdateVideoMetaText();
            DataContext = null;
            DataContext = CurrentVideo;
            ApplyLiveChatLayout();
            UpdateChannelButtonVisibility();
            UpdateDescriptionRichText();
            Bindings.Update();
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

        private async void BackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            // Save a synchronous fallback first so Home can immediately read the new progress
            // even if WebView2 refuses a final JavaScript snapshot while navigating away.
            RecordApproximatePlaybackProgress(forceVisibleProgress: true);
            await RequestProgressSnapshotAsync();
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
                    string.IsNullOrWhiteSpace(CurrentVideo.Description) &&
                    string.IsNullOrWhiteSpace(CurrentVideoMetaText)
                        ? Visibility.Collapsed
                        : Visibility.Visible;
            }

            if (DescriptionToggleButton != null)
            {
                DescriptionToggleButton.Visibility =
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
            if (PlayerBorder != null)
            {
                PlayerBorder.SizeChanged -= PlayerBorder_SizeChanged;
                PlayerBorder.SizeChanged += PlayerBorder_SizeChanged;
            }

            if (MainLayoutGrid != null)
            {
                MainLayoutGrid.SizeChanged -= MainLayoutGrid_SizeChanged;
                MainLayoutGrid.SizeChanged += MainLayoutGrid_SizeChanged;
            }

            if (XamlRoot != null)
            {
                XamlRoot.Changed += XamlRoot_Changed;

                UpdateLayoutForWidth();
            }

            UpdateLiveChatFrameSize();
        }

        private void PlayerBorder_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            UpdateLiveChatFrameSize();
        }

        private void MainLayoutGrid_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            UpdatePlayerSize();
            UpdateLiveChatFrameSize();
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
            UpdateLiveChatFrameSize();
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
            UpdateLiveChatFrameSize();
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
            UpdateLiveChatFrameSize();
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
                UpdateLiveChatFrameSize();
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

            UpdateLiveChatFrameSize();
        }

        private void UpdateLiveChatFrameSize()
        {
            if (PlayerBorder == null)
                return;

            double playerHeight =
                PlayerBorder.ActualHeight > 0
                    ? PlayerBorder.ActualHeight
                    : PlayerBorder.Height;

            if (double.IsNaN(playerHeight) ||
                double.IsInfinity(playerHeight) ||
                playerHeight <= 0)
            {
                return;
            }

            // The chat WebView frame itself should match the video player height.
            // The chat title/refresh row sits above it, just like the Back button row
            // sits above the player, so the frame top lines up with PlayerBorder.
            double frameHeight =
                Math.Max(180, playerHeight);

            if (LiveChatPanel != null)
            {
                LiveChatPanel.Height = double.NaN;
                LiveChatPanel.MaxHeight = double.PositiveInfinity;
            }

            if (LiveChatWebViewFrame != null)
            {
                LiveChatWebViewFrame.Height = frameHeight;
                LiveChatWebViewFrame.MaxHeight = frameHeight;
                LiveChatWebViewFrame.MinHeight = Math.Min(160, frameHeight);
            }

            if (LiveChatWebView != null)
            {
                LiveChatWebView.Height = frameHeight;
                LiveChatWebView.MaxHeight = frameHeight;
                LiveChatWebView.MinHeight = Math.Min(160, frameHeight);
            }

            if (LiveChatMessagesScrollViewer != null)
            {
                LiveChatMessagesScrollViewer.Height = frameHeight;
                LiveChatMessagesScrollViewer.MaxHeight = frameHeight;
                LiveChatMessagesScrollViewer.MinHeight = Math.Min(160, frameHeight);
            }
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

                bool shouldPlayLiveEdge =
                    CurrentVideo.IsLive ||
                    LooksLikeLiveVideo(CurrentVideo);

                double resumeSeconds =
                    shouldPlayLiveEdge || CurrentVideo.IsPremiere
                        ? 0
                        : Math.Max(
                            CurrentVideo.ResumeSeconds,
                            WatchHistoryService.GetResumeSeconds(CurrentVideo.Id));

                string startParameter =
                    !shouldPlayLiveEdge && resumeSeconds >= 5
                        ? $"&startSeconds={Math.Floor(resumeSeconds).ToString(CultureInfo.InvariantCulture)}"
                        : "";

                string liveParameter =
                    shouldPlayLiveEdge
                        ? "&live=1"
                        : "";

                playbackStartedAt = DateTimeOffset.Now;
                playbackStartedResumeSeconds = resumeSeconds;
                playerLoadedForLiveMode = shouldPlayLiveEdge;

                PlayerWebView.CoreWebView2.Navigate(
                    $"https://smoothtube.local/youtube-player.html?videoId={videoId}{startParameter}{liveParameter}");
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

            string rootPlayerPath =
                Path.Combine(AppContext.BaseDirectory, "youtube-player.html");

            string assetsPath =
                Path.Combine(AppContext.BaseDirectory, "Assets");

            string mappedPath =
                File.Exists(rootPlayerPath)
                    ? AppContext.BaseDirectory
                    : assetsPath;

            System.Diagnostics.Debug.WriteLine(
                $"SmoothTube player host mapped to: {mappedPath}");

            PlayerWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "smoothtube.local",
                mappedPath,
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
            string message;

            try
            {
                message = args.TryGetWebMessageAsString();
            }
            catch (ArgumentException)
            {
                message = args.WebMessageAsJson;
            }

            if (message == "ended")
            {
                WatchHistoryService.RecordCompleted(CurrentVideo);

                if (AutoPlayUpNextSwitch.IsOn &&
                    RecommendedVideos.Count > 0)
                {
                    NavigateToVideo(RecommendedVideos[0]);
                }

                return;
            }

            HandlePlayerProgressMessage(message);
        }

        private void HandlePlayerProgressMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(message);

                JsonElement root = document.RootElement;

                if (root.ValueKind == JsonValueKind.String)
                {
                    string nested = root.GetString() ?? "";

                    if (string.IsNullOrWhiteSpace(nested) ||
                        nested == message)
                    {
                        return;
                    }

                    HandlePlayerProgressMessage(nested);
                    return;
                }

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                string type =
                    root.TryGetProperty("type", out JsonElement typeElement)
                        ? typeElement.GetString() ?? ""
                        : "";

                if (type.Equals("ended", StringComparison.OrdinalIgnoreCase))
                {
                    WatchHistoryService.RecordCompleted(CurrentVideo);
                    return;
                }

                double currentSeconds =
                    GetJsonDouble(root, "currentTime");

                double durationSeconds =
                    GetJsonDouble(root, "duration");

                if (currentSeconds <= 0 ||
                    CurrentVideo.IsLive ||
                    CurrentVideo.IsPremiere)
                {
                    return;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"SmoothTube progress message | Video: {CurrentVideo.Id} | Current: {currentSeconds} | Duration: {durationSeconds} | Type: {type}");

                WatchHistoryService.RecordProgress(
                    CurrentVideo,
                    currentSeconds,
                    durationSeconds);
            }
            catch (JsonException)
            {
            }
        }

        private static double GetJsonDouble(
            JsonElement root,
            string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement element))
            {
                return 0;
            }

            return element.ValueKind switch
            {
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.String when double.TryParse(
                    element.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value) => value,
                _ => 0
            };
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
            if (isStoppingPlayback)
            {
                return;
            }

            isStoppingPlayback = true;

            try
            {
                progressTimer?.Stop();
                progressTimer = null;
                RecordApproximatePlaybackProgress();
                _ = RequestProgressSnapshotAsync();

                if (PlayerWebView.CoreWebView2 != null)
                {
                    _ = PlayerWebView.ExecuteScriptAsync(
                        "if (window.__smoothTubeReportProgress) { window.__smoothTubeReportProgress(); } document.querySelector('video')?.pause();");

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
            finally
            {
                isStoppingPlayback = false;
            }

            ApplyPlayerFullScreen(false);
        }

        private async void PlayerWebView_NavigationCompleted(
            WebView2 sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!args.IsSuccess ||
                PlayerWebView.CoreWebView2 == null ||
                PlayerWebView.Source?.ToString().Contains(
                    "smoothtube.local/youtube-player.html",
                    StringComparison.OrdinalIgnoreCase) != true)
            {
                return;
            }

            await InstallPlayerProgressBridgeAsync();
            StartProgressTimer();

            if (CurrentVideo.IsLive || LooksLikeLiveVideo(CurrentVideo))
            {
                await Task.Delay(700);

                try
                {
                    await PlayerWebView.CoreWebView2.ExecuteScriptAsync(
                        "window.__smoothTubeForceAutoplay && window.__smoothTubeForceAutoplay();");
                }
                catch (InvalidOperationException)
                {
                }
                catch (COMException)
                {
                }
            }
        }

        private async Task InstallPlayerProgressBridgeAsync()
        {
            if (PlayerWebView.CoreWebView2 == null ||
                CurrentVideo.IsLive ||
                CurrentVideo.IsPremiere)
            {
                return;
            }

            double resumeSeconds =
                Math.Max(
                    CurrentVideo.ResumeSeconds,
                    WatchHistoryService.GetResumeSeconds(CurrentVideo.Id));

            string resumeText =
                resumeSeconds.ToString(CultureInfo.InvariantCulture);

            string script =
                $@"(() => {{
                    const resumeSeconds = {resumeText};

                    function getPlayer() {{
                        return window.player || window.ytPlayer || window.youtubePlayer || null;
                    }}

                    function reportProgress(type) {{
                        try {{
                            const player = getPlayer();

                            if (!player || typeof player.getCurrentTime !== 'function') {{
                                return;
                            }}

                            const currentTime = Number(player.getCurrentTime() || 0);
                            const duration =
                                typeof player.getDuration === 'function'
                                    ? Number(player.getDuration() || 0)
                                    : 0;

                            if (window.chrome && window.chrome.webview) {{
                                window.chrome.webview.postMessage(JSON.stringify({{
                                    type: type || 'progress',
                                    currentTime,
                                    duration
                                }}));
                            }}
                        }} catch (error) {{
                        }}
                    }}

                    function applyResume(seconds) {{
                        try {{
                            if (!seconds || seconds < 5 || window.__smoothTubeResumeApplied) {{
                                return false;
                            }}

                            const player = getPlayer();

                            if (!player || typeof player.seekTo !== 'function') {{
                                return false;
                            }}

                            player.seekTo(seconds, true);
                            window.__smoothTubeResumeApplied = true;
                            reportProgress('resumed');
                            return true;
                        }} catch (error) {{
                            return false;
                        }}
                    }}

                    window.__smoothTubeReportProgress = () => reportProgress('progress');
                    window.__smoothTubeApplyResume = applyResume;

                    if (!window.__smoothTubeProgressBridgeInstalled) {{
                        window.__smoothTubeProgressBridgeInstalled = true;

                        window.addEventListener('beforeunload', () => {{
                            reportProgress('progress');
                        }});

                        setInterval(() => {{
                            reportProgress('progress');
                        }}, 5000);
                    }}

                    const resumeInterval = setInterval(() => {{
                        if (applyResume(resumeSeconds)) {{
                            clearInterval(resumeInterval);
                        }}
                    }}, 500);

                    setTimeout(() => {{
                        clearInterval(resumeInterval);
                    }}, 15000);
                }})();";

            try
            {
                await PlayerWebView.ExecuteScriptAsync(script);
            }
            catch (InvalidOperationException)
            {
            }
            catch (COMException)
            {
            }
        }

        private void StartProgressTimer()
        {
            progressTimer?.Stop();

            if (CurrentVideo.IsLive ||
                CurrentVideo.IsPremiere)
            {
                return;
            }

            progressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };

            progressTimer.Tick += async (_, _) =>
            {
                await RequestProgressSnapshotAsync();
                RecordApproximatePlaybackProgress();
            };

            progressTimer.Start();
        }

        private void RecordApproximatePlaybackProgress(bool forceVisibleProgress = false)
        {
            if (CurrentVideo.IsLive ||
                CurrentVideo.IsPremiere ||
                playbackStartedAt == null ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                return;
            }

            double elapsedSeconds =
                Math.Max(0, (DateTimeOffset.Now - playbackStartedAt.Value).TotalSeconds);

            double currentSeconds =
                Math.Max(0, playbackStartedResumeSeconds + elapsedSeconds);

            if (currentSeconds < 5 && !forceVisibleProgress)
            {
                return;
            }

            if (forceVisibleProgress && currentSeconds < 5)
            {
                currentSeconds = 5;
            }

            double durationSeconds =
                CurrentVideo.DurationSeconds > 0
                    ? CurrentVideo.DurationSeconds
                    : ParseDurationSeconds(CurrentVideo.Duration);

            System.Diagnostics.Debug.WriteLine(
                $"SmoothTube approximate progress | Video: {CurrentVideo.Id} | Current: {currentSeconds} | Duration: {durationSeconds}");

            WatchHistoryService.RecordProgress(
                CurrentVideo,
                currentSeconds,
                durationSeconds);
        }

        private static double ParseDurationSeconds(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            string[] parts =
                value
                    .Trim()
                    .Split(':', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0 || parts.Length > 3)
            {
                return 0;
            }

            double totalSeconds = 0;

            foreach (string part in parts)
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    return 0;
                }

                totalSeconds = totalSeconds * 60 + parsed;
            }

            return totalSeconds;
        }

        private async Task RequestProgressSnapshotAsync()
        {
            if (PlayerWebView?.CoreWebView2 == null ||
                CurrentVideo.IsLive ||
                CurrentVideo.IsPremiere)
            {
                return;
            }

            try
            {
                string result = await PlayerWebView.ExecuteScriptAsync(
                    "JSON.stringify(window.__smoothTubeGetProgress ? window.__smoothTubeGetProgress('snapshot') : null)");

                if (!string.IsNullOrWhiteSpace(result) &&
                    result != "null")
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"SmoothTube snapshot result for {CurrentVideo.Id}: {result}");

                    HandlePlayerProgressMessage(result);
                    return;
                }

                await PlayerWebView.ExecuteScriptAsync(
                    "if (window.__smoothTubeReportProgress) { window.__smoothTubeReportProgress(); }");
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"SmoothTube snapshot skipped: {ex.Message}");
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"SmoothTube snapshot failed: {ex.Message}");
            }
        }


        private void MockThumbnailImage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Image image)
            {
                return;
            }

            string thumbnail = "";

            if (image.DataContext is VideoItem video)
            {
                thumbnail = video.Thumbnail;
            }

            if (string.IsNullOrWhiteSpace(thumbnail))
            {
                thumbnail = CurrentVideo?.Thumbnail ?? "";
            }

            if (thumbnail.StartsWith("//", StringComparison.Ordinal))
            {
                thumbnail = "https:" + thumbnail;
            }

            if (Uri.TryCreate(thumbnail, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == "https" ||
                 uri.Scheme == "http" ||
                 uri.Scheme == "ms-appx" ||
                 uri.Scheme == "file"))
            {
                image.Source = new BitmapImage(uri);
            }
        }

        private void ChannelAvatarImage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            UpdateChannelAvatarImage();
        }

        private void ChannelAvatarImage_ImageFailed(
            object sender,
            ExceptionRoutedEventArgs e)
        {
            if (sender is Image image)
            {
                image.Source = null;
            }
        }

        private void UpdateChannelAvatarImage()
        {
            if (ChannelAvatarImage == null)
            {
                return;
            }

            string thumbnail = CurrentChannelThumbnail;

            if (thumbnail.StartsWith("//", StringComparison.Ordinal))
            {
                thumbnail = "https:" + thumbnail;
            }

            if (Uri.TryCreate(thumbnail, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == "https" ||
                 uri.Scheme == "http" ||
                 uri.Scheme == "ms-appx" ||
                 uri.Scheme == "file"))
            {
                ChannelAvatarImage.Source = new BitmapImage(uri)
                {
                    DecodePixelWidth = 88
                };
            }
            else
            {
                ChannelAvatarImage.Source = null;
            }
        }

        private void ResetChannelPresentation()
        {
            CurrentChannelThumbnail = "";
            CurrentChannelSubscriberText = "";
            UpdateChannelAvatarImage();
        }

        private void UpdateVideoMetaText()
        {
            List<string> parts = [];

            if (!string.IsNullOrWhiteSpace(CurrentVideo.PublishedAt))
            {
                parts.Add(CurrentVideo.PublishedAt);
            }

            if (!string.IsNullOrWhiteSpace(CurrentVideo.Views))
            {
                parts.Add(CurrentVideo.Views);
            }

            CurrentVideoMetaText = string.Join(" • ", parts);
        }

        private async Task LoadChannelPresentationAsync()
        {
            if (CurrentVideo == null ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentVideo.ChannelId) &&
                !string.IsNullOrWhiteSpace(CurrentVideo.Channel))
            {
                await TryResolveCurrentChannelAsync();
            }

            if (!string.IsNullOrWhiteSpace(CurrentVideo.ChannelId))
            {
                bool loadedFromApi = await TryLoadChannelPresentationFromApiAsync(CurrentVideo.ChannelId);

                if (!loadedFromApi)
                {
                    ChannelItem? channel = await ServiceLocator.YouTube.GetChannelAsync(CurrentVideo.ChannelId);

                    if (channel != null)
                    {
                        if (!string.IsNullOrWhiteSpace(channel.Thumbnail))
                        {
                            CurrentChannelThumbnail = channel.Thumbnail;
                        }

                        if (!string.IsNullOrWhiteSpace(channel.Title) &&
                            string.IsNullOrWhiteSpace(CurrentVideo.Channel))
                        {
                            CurrentVideo.Channel = channel.Title;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(CurrentChannelThumbnail) ||
                string.IsNullOrWhiteSpace(CurrentChannelSubscriberText) ||
                string.IsNullOrWhiteSpace(CurrentVideo.Description) ||
                string.IsNullOrWhiteSpace(CurrentVideo.Views))
            {
                await TryEnrichCurrentVideoFromWatchPageAsync();
            }

            RefreshCurrentVideoBindings();
            UpdateChannelAvatarImage();
        }

        private static bool HasApiKey =>
            AppSettings.HasYouTubeApiKey;

        private static async Task<JsonDocument?> TryGetJsonDocumentAsync(string requestUri)
        {
            if (string.IsNullOrWhiteSpace(requestUri))
            {
                return null;
            }

            try
            {
                using HttpResponseMessage response =
                    await MetadataHttpClient.GetAsync(requestUri);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using Stream stream =
                    await response.Content.ReadAsStreamAsync();

                return await JsonDocument.ParseAsync(stream);
            }
            catch (Exception ex) when (ex is HttpRequestException ||
                ex is TaskCanceledException ||
                ex is JsonException)
            {
                return null;
            }
        }

        private async Task<bool> TryLoadChannelPresentationFromApiAsync(string channelId)
        {
            if (!HasApiKey || string.IsNullOrWhiteSpace(channelId))
            {
                return false;
            }

            string requestUri =
                "https://www.googleapis.com/youtube/v3/channels" +
                "?part=snippet,statistics" +
                $"&id={Uri.EscapeDataString(channelId)}" +
                $"&key={Uri.EscapeDataString(AppSettings.YouTubeApiKey)}";

            using JsonDocument? document =
                await TryGetJsonDocumentAsync(requestUri);

            if (document == null)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("items", out JsonElement items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            JsonElement channel =
                items.EnumerateArray().FirstOrDefault();

            if (channel.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (channel.TryGetProperty("snippet", out JsonElement snippet))
            {
                string title = GetJsonString(snippet, "title");

                if (!string.IsNullOrWhiteSpace(title) &&
                    string.IsNullOrWhiteSpace(CurrentVideo.Channel))
                {
                    CurrentVideo.Channel = title;
                }

                string thumbnail = GetBestThumbnailFromSnippet(snippet);

                if (!string.IsNullOrWhiteSpace(thumbnail))
                {
                    CurrentChannelThumbnail = thumbnail;
                }
            }

            if (channel.TryGetProperty("statistics", out JsonElement statistics))
            {
                bool hiddenSubscriberCount =
                    statistics.TryGetProperty("hiddenSubscriberCount", out JsonElement hiddenElement) &&
                    hiddenElement.ValueKind == JsonValueKind.True;

                string subscriberCount = GetJsonString(statistics, "subscriberCount");

                if (!hiddenSubscriberCount &&
                    ulong.TryParse(
                        subscriberCount,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out ulong count))
                {
                    CurrentChannelSubscriberText = $"{FormatCompactCount(count)} subscribers";
                }
            }

            return !string.IsNullOrWhiteSpace(CurrentChannelThumbnail) ||
                !string.IsNullOrWhiteSpace(CurrentChannelSubscriberText);
        }

        private async Task TryEnrichCurrentVideoFromWatchPageAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                return;
            }

            try
            {
                using HttpResponseMessage response =
                    await MetadataHttpClient.GetAsync(
                        "https://www.youtube.com/watch" +
                        $"?v={Uri.EscapeDataString(CurrentVideo.Id)}" +
                        "&bpctr=9999999999&has_verified=1");

                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                string body = await response.Content.ReadAsStringAsync();

                string description = GetBestWatchPageDescription(body);

                if (!string.IsNullOrWhiteSpace(description) &&
                    ShouldReplaceDescription(CurrentVideo.Description, description))
                {
                    CurrentVideo.Description = description;
                }

                if (string.IsNullOrWhiteSpace(CurrentVideo.Views))
                {
                    string viewText = GetBestWatchPageViewText(body);

                    if (!string.IsNullOrWhiteSpace(viewText))
                    {
                        CurrentVideo.Views = viewText;
                    }
                }

                if (string.IsNullOrWhiteSpace(CurrentVideo.Channel))
                {
                    string ownerName = MatchJsonString(
                        body,
                        @"""ownerChannelName"":""(?<value>(?:\\.|[^""\\])*)""");

                    if (!string.IsNullOrWhiteSpace(ownerName))
                    {
                        CurrentVideo.Channel = ownerName;
                    }
                }

                if (string.IsNullOrWhiteSpace(CurrentChannelSubscriberText))
                {
                    string subscriberText = MatchJsonString(
                        body,
                        @"""subscriberCountText"":\{""simpleText"":""(?<value>(?:\\.|[^""\\])*)""");

                    if (!string.IsNullOrWhiteSpace(subscriberText))
                    {
                        CurrentChannelSubscriberText = subscriberText;
                    }
                }

                if (string.IsNullOrWhiteSpace(CurrentChannelThumbnail))
                {
                    string avatar = MatchBestYouTubeImageUrl(body, "yt3.ggpht.com");

                    if (string.IsNullOrWhiteSpace(avatar))
                    {
                        avatar = MatchBestYouTubeImageUrl(body, "yt3.googleusercontent.com");
                    }

                    if (!string.IsNullOrWhiteSpace(avatar))
                    {
                        CurrentChannelThumbnail = avatar;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException ||
                ex is TaskCanceledException ||
                ex is RegexMatchTimeoutException)
            {
            }
        }

        private static string GetJsonString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement property) &&
                property.ValueKind == JsonValueKind.String
                    ? property.GetString() ?? ""
                    : "";
        }

        private static string GetBestThumbnailFromSnippet(JsonElement snippet)
        {
            if (!snippet.TryGetProperty("thumbnails", out JsonElement thumbnails) ||
                thumbnails.ValueKind != JsonValueKind.Object)
            {
                return "";
            }

            foreach (string name in new[] { "high", "medium", "default" })
            {
                if (thumbnails.TryGetProperty(name, out JsonElement thumbnail) &&
                    thumbnail.TryGetProperty("url", out JsonElement urlElement) &&
                    urlElement.ValueKind == JsonValueKind.String)
                {
                    string url = urlElement.GetString() ?? "";

                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return url;
                    }
                }
            }

            return "";
        }

        private static string GetBestWatchPageDescription(string body)
        {
            string description = MatchJsonString(
                body,
                @"""shortDescription"":""(?<value>(?:\\.|[^""\\])*)""");

            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }

            description = MatchJsonString(
                body,
                @"""attributedDescriptionBodyText"":\{""content"":""(?<value>(?:\\.|[^""\\])*)""");

            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }

            description = MatchJsonString(
                body,
                @"""description"":\{""simpleText"":""(?<value>(?:\\.|[^""\\])*)""");

            return description.Trim();
        }

        private static bool ShouldReplaceDescription(string currentDescription, string candidateDescription)
        {
            if (string.IsNullOrWhiteSpace(candidateDescription))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(currentDescription))
            {
                return true;
            }

            string current = currentDescription.Trim();
            string candidate = candidateDescription.Trim();

            if (candidate.Length <= current.Length)
            {
                return false;
            }

            return current.EndsWith("...", StringComparison.Ordinal) ||
                candidate.Length > current.Length + 40;
        }

        private static string GetBestWatchPageViewText(string body)
        {
            string viewCount = MatchJsonString(
                body,
                @"""viewCount"":""(?<value>\d+)""");

            if (ulong.TryParse(
                viewCount,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ulong rawViewCount))
            {
                return rawViewCount.ToString("N0", CultureInfo.InvariantCulture) + " views";
            }

            string viewText = MatchJsonString(
                body,
                @"""viewCountText"":\{""simpleText"":""(?<value>(?:\\.|[^""\\])*)""");

            if (!string.IsNullOrWhiteSpace(viewText))
            {
                return NormalizeViewText(viewText);
            }

            viewText = MatchJsonString(
                body,
                @"""viewCount"":\{""simpleText"":""(?<value>(?:\\.|[^""\\])*)""");

            return NormalizeViewText(viewText);
        }

        private static string NormalizeViewText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            value = value.Trim();

            return value.Contains("view", StringComparison.OrdinalIgnoreCase)
                ? value
                : value + " views";
        }

        private static string MatchJsonString(string input, string pattern)
        {
            if (string.IsNullOrWhiteSpace(input) ||
                string.IsNullOrWhiteSpace(pattern))
            {
                return "";
            }

            try
            {
                Match match = Regex.Match(
                    input,
                    pattern,
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(500));

                if (!match.Success)
                {
                    return "";
                }

                string raw = match.Groups["value"].Value;

                if (string.IsNullOrWhiteSpace(raw))
                {
                    return "";
                }

                return JsonSerializer.Deserialize<string>("\"" + raw + "\"") ?? "";
            }
            catch (Exception ex) when (ex is JsonException ||
                ex is RegexMatchTimeoutException)
            {
                return "";
            }
        }

        private static string MatchBestYouTubeImageUrl(string input, string host)
        {
            if (string.IsNullOrWhiteSpace(input) ||
                string.IsNullOrWhiteSpace(host))
            {
                return "";
            }

            try
            {
                MatchCollection matches = Regex.Matches(
                    input,
                    @"https:\/\/" + Regex.Escape(host) + @"\/[^""\\]+",
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(700));

                return matches
                    .Select(match => match.Value.Replace("\\/", "/"))
                    .Select(url => url.Replace("\\u0026", "&"))
                    .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
                    .OrderByDescending(url => url.Contains("=s176", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(url => url.Contains("=s160", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault() ?? "";
            }
            catch (RegexMatchTimeoutException)
            {
                return "";
            }
        }

        private static string FormatCompactCount(ulong count)
        {
            return count switch
            {
                >= 1_000_000_000 => $"{TrimCount(count / 1_000_000_000d)}B",
                >= 1_000_000 => $"{TrimCount(count / 1_000_000d)}M",
                >= 1_000 => $"{TrimCount(count / 1_000d)}K",
                _ => count.ToString("N0", CultureInfo.InvariantCulture)
            };
        }

        private static string TrimCount(double value)
        {
            return value.ToString(value >= 10 ? "0.#" : "0.##", CultureInfo.InvariantCulture);
        }

        private async void RefreshLiveChat_Click(
            object sender,
            RoutedEventArgs e)
        {
            await LoadLiveChatAsync();
        }

        private void UpdateDescriptionRichText()
        {
            string description = CurrentVideo?.Description ?? "";

            descriptionRichTextBuilt = false;
            descriptionExpanded = false;

            if (DescriptionPreviewTextBlock != null)
            {
                DescriptionPreviewTextBlock.Text = description;
                DescriptionPreviewTextBlock.Visibility = Visibility.Visible;
            }

            if (DescriptionRichTextBlock != null)
            {
                DescriptionRichTextBlock.Blocks.Clear();
                DescriptionRichTextBlock.Visibility = Visibility.Collapsed;
            }

            if (DescriptionToggleButton != null)
            {
                DescriptionToggleButton.Content = "Show more";
            }
        }

        private void BuildDescriptionRichTextIfNeeded()
        {
            if (descriptionRichTextBuilt ||
                DescriptionRichTextBlock == null)
            {
                return;
            }

            DescriptionRichTextBlock.Blocks.Clear();

            string description = CurrentVideo?.Description ?? "";

            if (string.IsNullOrWhiteSpace(description))
            {
                return;
            }

            var paragraph = new Paragraph();
            AddDescriptionInlines(paragraph, description);
            DescriptionRichTextBlock.Blocks.Add(paragraph);
            descriptionRichTextBuilt = true;
        }

        private static void AddDescriptionInlines(
            Paragraph paragraph,
            string text)
        {
            Regex urlRegex = new(
                @"(?<url>(?:https?://|www\.)[^\s<>()]+)",
                RegexOptions.IgnoreCase);

            int currentIndex = 0;

            foreach (Match match in urlRegex.Matches(text))
            {
                if (match.Index > currentIndex)
                {
                    AddTextRuns(
                        paragraph,
                        text[currentIndex..match.Index]);
                }

                string matchedUrl = match.Groups["url"].Value;
                string trailingText = "";

                while (matchedUrl.Length > 0 &&
                    ".,;:!?)］】}".Contains(matchedUrl[^1]))
                {
                    trailingText = matchedUrl[^1] + trailingText;
                    matchedUrl = matchedUrl[..^1];
                }

                string normalizedUrl = matchedUrl.StartsWith(
                    "www.",
                    StringComparison.OrdinalIgnoreCase)
                        ? "https://" + matchedUrl
                        : matchedUrl;

                if (Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri? uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp ||
                     uri.Scheme == Uri.UriSchemeHttps))
                {
                    var hyperlink = new Hyperlink
                    {
                        NavigateUri = uri
                    };

                    hyperlink.Inlines.Add(
                        new Run
                        {
                            Text = matchedUrl
                        });

                    paragraph.Inlines.Add(hyperlink);
                }
                else
                {
                    AddTextRuns(paragraph, match.Groups["url"].Value);
                }

                if (!string.IsNullOrEmpty(trailingText))
                {
                    AddTextRuns(paragraph, trailingText);
                }

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < text.Length)
            {
                AddTextRuns(paragraph, text[currentIndex..]);
            }
        }

        private static void AddTextRuns(
            Paragraph paragraph,
            string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string normalizedText = text.Replace("\r\n", "\n");
            string[] lines = normalizedText.Split('\n');

            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Length > 0)
                {
                    paragraph.Inlines.Add(
                        new Run
                        {
                            Text = lines[index]
                        });
                }

                if (index < lines.Length - 1)
                {
                    paragraph.Inlines.Add(new LineBreak());
                }
            }
        }

        private void DescriptionToggleButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            descriptionExpanded = !descriptionExpanded;

            if (descriptionExpanded)
            {
                BuildDescriptionRichTextIfNeeded();

                if (DescriptionPreviewTextBlock != null)
                {
                    DescriptionPreviewTextBlock.Visibility = Visibility.Collapsed;
                }

                if (DescriptionRichTextBlock != null)
                {
                    DescriptionRichTextBlock.Visibility = Visibility.Visible;
                }

                DescriptionToggleButton.Content = "Show less";
                return;
            }

            if (DescriptionRichTextBlock != null)
            {
                DescriptionRichTextBlock.Visibility = Visibility.Collapsed;

                // Clearing the rich text removes a large amount of inline layout work while scrolling.
                DescriptionRichTextBlock.Blocks.Clear();
            }

            descriptionRichTextBuilt = false;

            if (DescriptionPreviewTextBlock != null)
            {
                DescriptionPreviewTextBlock.Visibility = Visibility.Visible;
            }

            DescriptionToggleButton.Content = "Show more";
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
                image.DataContext is not VideoItem video)
            {
                return;
            }

            image.ImageFailed -= UpNextThumbnail_ImageFailed;
            image.ImageFailed += UpNextThumbnail_ImageFailed;
            image.SizeChanged -= UpNextThumbnail_SizeChanged;
            image.SizeChanged += UpNextThumbnail_SizeChanged;

            ApplyUpNextThumbnailCrop(image);

            List<string> candidates =
                BuildUpNextThumbnailCandidates(video);

            image.Tag = candidates;
            SetUpNextThumbnailSource(image, candidates, 0);
        }

        private void UpNextThumbnail_ImageFailed(
            object sender,
            ExceptionRoutedEventArgs e)
        {
            if (sender is not Image image ||
                image.Tag is not List<string> candidates)
            {
                return;
            }

            string current =
                (image.Source as BitmapImage)?.UriSource?.ToString() ?? "";

            int nextIndex =
                Math.Max(0, candidates.FindIndex(item =>
                    item.Equals(current, StringComparison.OrdinalIgnoreCase))) + 1;

            SetUpNextThumbnailSource(image, candidates, nextIndex);
        }

        private void UpNextThumbnail_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            if (sender is Image image)
            {
                ApplyUpNextThumbnailCrop(image);
            }
        }

        private static void ApplyUpNextThumbnailCrop(Image image)
        {
            image.RenderTransformOrigin = new Point(0.5, 0.5);

            const double baseThumbnailScale = 1.055;

            if (image.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = baseThumbnailScale;
                scale.ScaleY = baseThumbnailScale;
            }
            else
            {
                image.RenderTransform = new ScaleTransform
                {
                    ScaleX = baseThumbnailScale,
                    ScaleY = baseThumbnailScale
                };
            }

            double width = image.ActualWidth > 0 ? image.ActualWidth : image.Width;
            double height = image.ActualHeight > 0 ? image.ActualHeight : image.Height;

            if (width > 0 && height > 0)
            {
                image.Clip = new RectangleGeometry
                {
                    Rect = new Rect(0, 0, width, height)
                };
            }
        }

        private static void SetUpNextThumbnailSource(
            Image image,
            List<string> candidates,
            int startIndex)
        {
            for (int i = Math.Max(0, startIndex); i < candidates.Count; i++)
            {
                string thumbnail = candidates[i];

                if (Uri.TryCreate(thumbnail, UriKind.Absolute, out Uri? uri) &&
                    (uri.Scheme == "https" ||
                     uri.Scheme == "http" ||
                     uri.Scheme == "ms-appx" ||
                     uri.Scheme == "file"))
                {
                    image.Source = new BitmapImage(uri)
                    {
                        DecodePixelWidth = 280
                    };

                    return;
                }
            }

            image.Source = null;
        }

        private static List<string> BuildUpNextThumbnailCandidates(
            VideoItem video)
        {
            List<string> urls = [];

            string videoId = video.Id ?? "";
            string original = video.Thumbnail ?? "";

            if (original.StartsWith("//", StringComparison.Ordinal))
            {
                original = "https:" + original;
            }

            string extractedId =
                ExtractYouTubeThumbnailVideoId(original);

            if (string.IsNullOrWhiteSpace(videoId))
            {
                videoId = extractedId;
            }

            if (!string.IsNullOrWhiteSpace(videoId))
            {
                // Prefer the clean 16:9 feed thumbnails first.
                // maxresdefault can exist but still contain awkward/baked-in bars,
                // so keep it after hq720/sddefault instead of using it first.
                urls.Add($"https://i.ytimg.com/vi/{videoId}/hq720.jpg");
                urls.Add($"https://i.ytimg.com/vi_webp/{videoId}/hq720.webp");
                urls.Add($"https://i.ytimg.com/vi/{videoId}/sddefault.jpg");
                urls.Add($"https://i.ytimg.com/vi_webp/{videoId}/sddefault.webp");
                urls.Add($"https://i.ytimg.com/vi/{videoId}/maxresdefault.jpg");
                urls.Add($"https://i.ytimg.com/vi_webp/{videoId}/maxresdefault.webp");
            }

            if (!string.IsNullOrWhiteSpace(original))
            {
                urls.Add(original);
            }

            if (!string.IsNullOrWhiteSpace(videoId))
            {
                urls.Add($"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg");
                urls.Add($"https://i.ytimg.com/vi/{videoId}/mqdefault.jpg");
                urls.Add($"https://i.ytimg.com/vi/{videoId}/default.jpg");
            }

            return urls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ExtractYouTubeThumbnailVideoId(
            string thumbnail)
        {
            if (string.IsNullOrWhiteSpace(thumbnail) ||
                !Uri.TryCreate(thumbnail, UriKind.Absolute, out Uri? uri))
            {
                return "";
            }

            string[] parts =
                uri.AbsolutePath
                    .Split('/', StringSplitOptions.RemoveEmptyEntries);

            int viIndex =
                Array.FindIndex(parts, part =>
                    part.Equals("vi", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("vi_webp", StringComparison.OrdinalIgnoreCase));

            return viIndex >= 0 && viIndex + 1 < parts.Length
                ? parts[viIndex + 1]
                : "";
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

            // Merge enriched metadata without wiping the data that came from the card/history.
            // Some enrichment paths can legitimately return only duration/thumbnail, so direct
            // assignment here can blank the title/channel/description on the video page.
            if (!string.IsNullOrWhiteSpace(enrichedVideo.Title))
            {
                CurrentVideo.Title = enrichedVideo.Title;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Channel))
            {
                CurrentVideo.Channel = enrichedVideo.Channel;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.ChannelId))
            {
                CurrentVideo.ChannelId = enrichedVideo.ChannelId;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Views))
            {
                CurrentVideo.Views = enrichedVideo.Views;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Likes))
            {
                CurrentVideo.Likes = enrichedVideo.Likes;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Duration))
            {
                CurrentVideo.Duration = enrichedVideo.Duration;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.PublishedAt))
            {
                CurrentVideo.PublishedAt = enrichedVideo.PublishedAt;
            }

            if (enrichedVideo.PublishedAtSort != null)
            {
                CurrentVideo.PublishedAtSort = enrichedVideo.PublishedAtSort;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Thumbnail))
            {
                CurrentVideo.Thumbnail = enrichedVideo.Thumbnail;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Description))
            {
                CurrentVideo.Description = enrichedVideo.Description;
            }

            // Live/premiere flags are meaningful even when text fields are empty.
            CurrentVideo.IsLive = enrichedVideo.IsLive;
            CurrentVideo.IsPremiere = enrichedVideo.IsPremiere;
            CurrentVideo.LiveChatId = enrichedVideo.LiveChatId;

            if (enrichedVideo.IsEmbeddable)
            {
                CurrentVideo.IsEmbeddable = true;
            }

            // Preserve local continue-watching progress after metadata enrichment.
            WatchHistoryService.ApplySavedProgress(CurrentVideo);

            EnsureCurrentVideoDefaults();
            RefreshCurrentVideoBindings();
            _ = UpdateSubscriptionButtonAsync();
            _ = LoadChannelPresentationAsync();

            WatchHistoryService.UpdateMetadata(CurrentVideo);

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
                string.IsNullOrWhiteSpace(video.Views) ||
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

            await EnsureCurrentVideoLiveStatusAsync();

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

        private async Task EnsureCurrentVideoLiveStatusAsync()
        {
            if (CurrentVideo.Category != "YouTube" ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                return;
            }

            try
            {
                // RSS subscription entries do not reliably expose live/premiere/chat state.
                // Check only the video the user actually opened, so Subscriptions remains
                // RSS-first and lightweight while VideoPage can still enable live chat.
                VideoItem? enrichedVideo =
                    await ServiceLocator.YouTube.GetVideoAsync(CurrentVideo.Id);

                if (enrichedVideo == null)
                    return;

                bool inferredLive =
                    LooksLikeLiveVideo(CurrentVideo) ||
                    LooksLikeLiveVideo(enrichedVideo);

                CurrentVideo.IsLive = enrichedVideo.IsLive || inferredLive;
                CurrentVideo.IsPremiere = !CurrentVideo.IsLive && enrichedVideo.IsPremiere;
                CurrentVideo.LiveChatId = enrichedVideo.LiveChatId;

                if (CurrentVideo.IsLive && string.IsNullOrWhiteSpace(CurrentVideo.Duration))
                {
                    CurrentVideo.Duration = "LIVE";
                }

                if (!string.IsNullOrWhiteSpace(enrichedVideo.Duration))
                    CurrentVideo.Duration = enrichedVideo.Duration;

                if (!string.IsNullOrWhiteSpace(enrichedVideo.Views))
                    CurrentVideo.Views = enrichedVideo.Views;

                if (!string.IsNullOrWhiteSpace(enrichedVideo.Likes))
                    CurrentVideo.Likes = enrichedVideo.Likes;

                if (!string.IsNullOrWhiteSpace(enrichedVideo.ChannelId))
                    CurrentVideo.ChannelId = enrichedVideo.ChannelId;

                if (!string.IsNullOrWhiteSpace(enrichedVideo.Channel))
                    CurrentVideo.Channel = enrichedVideo.Channel;

                if (!string.IsNullOrWhiteSpace(enrichedVideo.PublishedAt))
                    CurrentVideo.PublishedAt = enrichedVideo.PublishedAt;

                if (enrichedVideo.PublishedAtSort.HasValue)
                    CurrentVideo.PublishedAtSort = enrichedVideo.PublishedAtSort;

                if (!string.IsNullOrWhiteSpace(enrichedVideo.Description) &&
                    string.IsNullOrWhiteSpace(CurrentVideo.Description))
                {
                    CurrentVideo.Description = enrichedVideo.Description;
                }

                if (!string.IsNullOrWhiteSpace(enrichedVideo.Thumbnail))
                    CurrentVideo.Thumbnail = enrichedVideo.Thumbnail;

                CurrentVideo.IsEmbeddable = enrichedVideo.IsEmbeddable;

                RefreshCurrentVideoBindings();

                // RecordStarted runs before RSS-only videos are enriched.
                // Once this single-video check discovers live/premiere state,
                // update Continue Watching so Home shows the LIVE/Premiere badge too.
                WatchHistoryService.UpdateMetadata(CurrentVideo);

                if (CurrentVideo.IsLive)
                {
                    CurrentVideo.ResumeSeconds = 0;
                    CurrentVideo.Progress = 0;
                    CurrentVideo.DurationSeconds = 0;

                    if (!playerLoadedForLiveMode)
                    {
                        await UpdatePlayerSourceAsync();
                    }

                    UpdateLiveChatFrameSize();
                }
            }
            catch (Exception ex) when (ex is HttpRequestException ||
                ex is TaskCanceledException ||
                ex is InvalidOperationException)
            {
                // Keep the RSS-provided video playable even if single-video live status
                // enrichment is unavailable. If the RSS title strongly looks live, still
                // mark it so Continue Watching and VideoPage badges stay useful.
                if (LooksLikeLiveVideo(CurrentVideo))
                {
                    CurrentVideo.IsLive = true;
                    CurrentVideo.IsPremiere = false;

                    if (string.IsNullOrWhiteSpace(CurrentVideo.Duration))
                    {
                        CurrentVideo.Duration = "LIVE";
                    }

                    RefreshCurrentVideoBindings();
                    WatchHistoryService.UpdateMetadata(CurrentVideo);
                }
            }
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

                if (LiveChatSignInButton != null)
                {
                    LiveChatSignInButton.Visibility =
                        liveChatSignInButtonDismissed
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                }

                UpdateLiveChatFrameSize();

                await LiveChatWebView.EnsureCoreWebView2Async();

                string videoId =
                    Uri.EscapeDataString(CurrentVideo.Id);

                LiveChatWebView.CoreWebView2.Navigate(
                    $"https://www.youtube.com/live_chat?is_popout=1&v={videoId}");

                LiveChatStatus = "";
                Bindings.Update();
                UpdateLiveChatStatusVisibility();
                UpdateLiveChatFrameSize();

                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    UpdatePlayerSize();
                    UpdateLiveChatFrameSize();
                    _ = ApplyLiveChatCompactLayoutAsync();
                    _ = UpdateLiveChatSignInButtonVisibilityAsync();
                });
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
            if (LiveChatWebViewFrame != null)
            {
                LiveChatWebViewFrame.Visibility = Visibility.Collapsed;
            }

            if (LiveChatMessagesScrollViewer != null)
            {
                LiveChatMessagesScrollViewer.Visibility = Visibility.Visible;
            }

            if (LiveChatSignInButton != null)
            {
                LiveChatSignInButton.Visibility = Visibility.Collapsed;
            }
        }

        private async void LiveChatSignInButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            liveChatSignInButtonDismissed = true;

            if (LiveChatSignInButton != null)
            {
                LiveChatSignInButton.Visibility = Visibility.Collapsed;
            }

            if (LiveChatWebView?.CoreWebView2 == null ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                return;
            }

            try
            {
                string clicked =
                    await LiveChatWebView.CoreWebView2.ExecuteScriptAsync(
                        "(() => {" +
                        "  const hasText = (node, text) => ((node?.innerText || node?.textContent || '').toLowerCase().includes(text));" +
                        "  const els = Array.from(document.querySelectorAll('a[href*=\\\"ServiceLogin\\\"],a[href*=\\\"accounts.google.com\\\"],button,yt-button-renderer,ytd-button-renderer,tp-yt-paper-button,.yt-spec-button-shape-next'));" +
                        "  const el = els.find(x => { const href = x.href || x.querySelector?.('a')?.href || ''; return href.includes('ServiceLogin') || href.includes('accounts.google.com') || hasText(x, 'sign in') || hasText(x, 'signin'); });" +
                        "  const target = el?.querySelector?.('a,button,tp-yt-paper-button') || el;" +
                        "  if (target) { target.click(); return true; }" +
                        "  return false;" +
                        "})()");

                if (clicked.Contains("true", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                ex is COMException)
            {
            }

            string continueUrl =
                Uri.EscapeDataString(
                    $"https://www.youtube.com/live_chat?is_popout=1&v={Uri.EscapeDataString(CurrentVideo.Id)}");

            LiveChatWebView.CoreWebView2.Navigate(
                $"https://accounts.google.com/ServiceLogin?continue={continueUrl}");
        }

        private async void LiveChatWebView_NavigationCompleted(
            WebView2 sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            await ApplyLiveChatCompactLayoutAsync();
            await UpdateLiveChatSignInButtonVisibilityAsync();
        }

        private async Task ApplyLiveChatCompactLayoutAsync()
        {
            if (LiveChatWebView?.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                // YouTube live chat sometimes reserves a tall, empty top header/ticker area
                // in the popout iframe. Keep messages/input intact, but collapse obvious
                // empty header/banner/ticker containers so the chat starts near the top.
                await Task.Delay(900);

                await LiveChatWebView.CoreWebView2.ExecuteScriptAsync(
                    "(() => {" +
                    "  const styleId = 'smoothtube-live-chat-compact-style';" +
                    "  let style = document.getElementById(styleId);" +
                    "  if (!style) {" +
                    "    style = document.createElement('style');" +
                    "    style.id = styleId;" +
                    "    style.textContent = `" +
                    "      yt-live-chat-header-renderer," +
                    "      yt-live-chat-ticker-renderer," +
                    "      yt-live-chat-banner-manager," +
                    "      yt-live-chat-banner-renderer," +
                    "      yt-live-chat-viewer-engagement-message-renderer," +
                    "      #header," +
                    "      #ticker," +
                    "      #banner," +
                    "      #separator," +
                    "      #chat-messages > yt-live-chat-banner-manager {" +
                    "        display: none !important;" +
                    "        height: 0 !important;" +
                    "        min-height: 0 !important;" +
                    "        max-height: 0 !important;" +
                    "        margin: 0 !important;" +
                    "        padding: 0 !important;" +
                    "        overflow: hidden !important;" +
                    "      }" +
                    "      yt-live-chat-item-list-renderer," +
                    "      #items," +
                    "      #item-scroller {" +
                    "        margin-top: 0 !important;" +
                    "        padding-top: 0 !important;" +
                    "      }" +
                    "      yt-live-chat-message-input-renderer," +
                    "      yt-live-chat-text-input-field-renderer," +
                    "      #input-panel," +
                    "      #input {" +
                    "        display: block !important;" +
                    "        visibility: visible !important;" +
                    "        height: auto !important;" +
                    "        min-height: unset !important;" +
                    "        max-height: none !important;" +
                    "        opacity: 1 !important;" +
                    "      }" +
                    "    `;" +
                    "    document.documentElement.appendChild(style);" +
                    "  }" +
                    "  const maybeEmptyNodes = Array.from(document.querySelectorAll('yt-live-chat-header-renderer,#header,#ticker,#banner'));" +
                    "  for (const node of maybeEmptyNodes) {" +
                    "    const text = (node.innerText || node.textContent || '').trim();" +
                    "    if (!text || text.length < 12) {" +
                    "      node.style.display = 'none';" +
                    "      node.style.height = '0px';" +
                    "      node.style.minHeight = '0px';" +
                    "      node.style.margin = '0';" +
                    "      node.style.padding = '0';" +
                    "      node.style.overflow = 'hidden';" +
                    "    }" +
                    "  }" +
                    "})()");
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                ex is COMException)
            {
                // Ignore compact-layout injection failures; YouTube chat should still work.
            }
        }

        private async Task UpdateLiveChatSignInButtonVisibilityAsync()
        {
            if (LiveChatSignInButton == null ||
                LiveChatWebView?.CoreWebView2 == null)
            {
                return;
            }

            if (liveChatSignInButtonDismissed)
            {
                LiveChatSignInButton.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                // Give YouTube's live chat app a moment to hydrate after navigation.
                await Task.Delay(1200);

                string result =
                    await LiveChatWebView.CoreWebView2.ExecuteScriptAsync(
                        "(() => {" +
                        "  const text = (document.body?.innerText || '').toLowerCase();" +
                        "  const hasInput = !!document.querySelector('yt-live-chat-text-input-field-renderer, #input, textarea, div[contenteditable=\\\"true\\\"]');" +
                        "  const signedInText = text.includes('say something') || text.includes('chat publicly') || text.includes('send a message');" +
                        "  const signInText = text.includes('sign in to chat') || text.includes('sign in');" +
                        "  if (hasInput || signedInText) return 'signed-in';" +
                        "  if (signInText) return 'signed-out';" +
                        "  return 'unknown';" +
                        "})()");

                if (result.Contains("signed-in", StringComparison.OrdinalIgnoreCase))
                {
                    liveChatSignInButtonDismissed = true;
                    LiveChatSignInButton.Visibility = Visibility.Collapsed;
                }
                else
                {
                    LiveChatSignInButton.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                ex is COMException)
            {
                if (liveChatSignInButtonDismissed)
                {
                    LiveChatSignInButton.Visibility = Visibility.Collapsed;
                }
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

            UpdateLiveChatFrameSize();
        }
    }
}
