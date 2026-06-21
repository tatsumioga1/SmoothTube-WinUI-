using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SmoothTube.Models;
using SmoothTube.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace SmoothTube
{
    public sealed partial class LibraryPage : Page
    {
        public ObservableCollection<VideoItem> ContinueWatchingVideos { get; } = [];

        public string StatusText { get; set; } = "";

        public LibraryPage()
        {
            InitializeComponent();
            Loaded += LibraryPage_Loaded;
        }

        private void LibraryPage_Loaded(
            object sender,
            Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ContinueWatchingVideos.Clear();

            foreach (VideoItem video in WatchHistoryService.GetContinueWatching())
            {
                ContinueWatchingVideos.Add(video);
            }

            StatusText =
                ContinueWatchingVideos.Count == 0
                    ? "Videos you start but do not finish will appear here."
                    : $"{ContinueWatchingVideos.Count} videos";

            Bindings.Update();
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
                        AllVideos = ContinueWatchingVideos.ToList()
                    });
            }
        }
    }
}
