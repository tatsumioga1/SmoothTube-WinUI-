using Microsoft.UI.Xaml;
using System;

namespace SmoothTube
{
    public sealed partial class YouTubeWatchWindow : Window
    {
        private readonly string videoId;

        public YouTubeWatchWindow(string videoId)
        {
            this.videoId = videoId;
            InitializeComponent();
            Activated += YouTubeWatchWindow_Activated;
        }

        private async void YouTubeWatchWindow_Activated(
            object sender,
            WindowActivatedEventArgs args)
        {
            Activated -= YouTubeWatchWindow_Activated;

            await WatchWebView.EnsureCoreWebView2Async();

            WatchWebView.CoreWebView2.Navigate(
                $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}");
        }
    }
}
