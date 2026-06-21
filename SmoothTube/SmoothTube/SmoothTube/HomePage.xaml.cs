using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SmoothTube.Models;
using SmoothTube.Services;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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
                Bindings.Update();
            }
            catch (System.Exception)
            {
                StatusText = "";
                LoadingVisibility = Visibility.Collapsed;
                VideosVisibility = Videos.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

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
