using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SmoothTube.Models;
using SmoothTube.Services;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SmoothTube
{
    public sealed partial class HomePage : Page
    {
        public ObservableCollection<VideoItem> Videos { get; } = [];

        public ObservableCollection<VideoItem> ContinueWatchingVideos { get; } = [];

        public ObservableCollection<int> SkeletonItems { get; } = [];

        public string PrimarySectionTitle { get; set; } = "Recommended";

        public string StatusText { get; set; } = "Loading videos...";

        public Visibility ContinueWatchingVisibility { get; set; } = Visibility.Collapsed;

        public Visibility LoadingVisibility { get; set; } = Visibility.Visible;

        public Visibility VideosVisibility { get; set; } = Visibility.Collapsed;

        public Visibility LoadMoreVisibility { get; set; } = Visibility.Collapsed;

        private bool isLoadingMore;

        public HomePage()
        {
            InitializeComponent();

            Loaded += HomePage_Loaded;
        }

        private async void HomePage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await LoadVideosAsync();
        }

        private async Task LoadVideosAsync()
        {
            StatusText = "Loading videos...";
            LoadingVisibility = Visibility.Visible;
            VideosVisibility = Visibility.Collapsed;

            PrimarySectionTitle = "Recommended";

            ReplaceVideos(
                ContinueWatchingVideos,
                WatchHistoryService.GetContinueWatching());

            ContinueWatchingVisibility =
                ContinueWatchingVideos.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (SkeletonItems.Count == 0)
            {
                for (int i = 0; i < 8; i++)
                {
                    SkeletonItems.Add(i);
                }
            }

            Bindings.Update();

            try
            {
                Task<List<VideoItem>> primaryTask =
                    ServiceLocator.YouTube.GetHomeVideosAsync();

                await primaryTask;
                ReplaceVideosIfAny(Videos, primaryTask.Result);
                StatusText = "";
                LoadingVisibility = Visibility.Collapsed;
                VideosVisibility = Visibility.Visible;
                LoadMoreVisibility =
                    Videos.Count > 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                Bindings.Update();
            }
            catch (System.Exception)
            {
                StatusText = "";
                LoadingVisibility = Visibility.Collapsed;
                VideosVisibility = Videos.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                LoadMoreVisibility = VideosVisibility;
            }

            Bindings.Update();
        }

        private async void LoadMoreButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoadingMore)
                return;

            isLoadingMore = true;
            StatusText = "Loading more recommendations...";
            LoadMoreVisibility = Visibility.Collapsed;
            Bindings.Update();

            try
            {
                List<VideoItem> moreVideos =
                    await ServiceLocator.YouTube.GetMoreHomeVideosAsync(
                        Videos.Select(video => video.Id));

                foreach (VideoItem video in moreVideos)
                {
                    if (Videos.All(item => item.Id != video.Id))
                    {
                        Videos.Add(video);
                    }
                }

                StatusText =
                    moreVideos.Count == 0
                        ? "No more recommendations returned right now."
                        : "";
            }
            catch (System.Exception)
            {
                StatusText = "Could not load more recommendations.";
            }

            isLoadingMore = false;
            LoadMoreVisibility = Visibility.Visible;
            Bindings.Update();
        }

        private static void ReplaceVideosIfAny(
            ObservableCollection<VideoItem> target,
            List<VideoItem> videos)
        {
            if (videos.Count > 0)
            {
                ReplaceVideos(target, videos);
            }
        }

        private static void ReplaceVideos(
            ObservableCollection<VideoItem> target,
            IEnumerable<VideoItem> videos)
        {
            target.Clear();

            foreach (VideoItem video in videos)
            {
                target.Add(video);
            }
        }

        private void VideoCard_Tapped(
            object sender,
            TappedRoutedEventArgs e)
        {
            if (sender is Border card &&
                card.DataContext is VideoItem video)
            {
                Frame.Navigate(
                    typeof(VideoPage),
                    new VideoNavigationData
                    {
                        CurrentVideo = video,
                        AllVideos = GetCurrentHomeVideos()
                    });
            }
        }

        private List<VideoItem> GetCurrentHomeVideos()
        {
            List<VideoItem> videos = [];
            videos.AddRange(Videos);
            videos.AddRange(ContinueWatchingVideos);
            return videos;
        }
    }
}
