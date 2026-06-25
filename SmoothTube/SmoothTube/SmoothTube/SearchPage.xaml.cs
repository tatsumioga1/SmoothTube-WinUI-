using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SmoothTube.Models;
using SmoothTube.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmoothTube
{
    public sealed partial class SearchPage : Page
    {
        private static string cachedQuery = "";
        private static string cachedStatusText = "Search for videos or channels.";
        private static readonly List<SearchResultItem> CachedResults = [];

        public ObservableCollection<SearchResultItem> Results { get; } = [];

        public bool IsSearching { get; set; }

        public Visibility LoadingVisibility { get; set; } = Visibility.Collapsed;

        public string StatusText { get; set; } = "Search for videos or channels.";

        private CancellationTokenSource? searchCancellation;

        public SearchPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
            Loaded += SearchPage_Loaded;
        }

        private void SearchPage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(cachedQuery) &&
                SearchBox.Text != cachedQuery)
            {
                SearchBox.Text = cachedQuery;
            }

            if (CachedResults.Count > 0 &&
                Results.Count == 0)
            {
                Results.Clear();

                foreach (SearchResultItem result in CachedResults)
                {
                    Results.Add(result);
                }

                StatusText = cachedStatusText;
                LoadingVisibility = Visibility.Collapsed;
                IsSearching = false;
                Bindings.Update();
            }
        }

        private async void SearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await SearchAsync(SearchBox.Text);
        }

        private async void SearchBox_KeyDown(
            object sender,
            KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await SearchAsync(SearchBox.Text);
            }
        }

        private async Task SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                StatusText = "Type something to search.";
                Bindings.Update();
                return;
            }

            searchCancellation?.Cancel();
            searchCancellation = new CancellationTokenSource();

            CancellationToken cancellationToken = searchCancellation.Token;

            IsSearching = true;
            LoadingVisibility = Visibility.Visible;
            StatusText = "Searching...";
            Bindings.Update();

            try
            {
                var results =
                    await ServiceLocator.YouTube.SearchAllAsync(
                        query,
                        cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    return;

                if (results.Count == 0)
                {
                    await Task.Delay(700, cancellationToken);

                    results =
                        await ServiceLocator.YouTube.SearchAllAsync(
                            query,
                            cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        return;
                }

                Results.Clear();

                foreach (SearchResultItem result in results)
                {
                    Results.Add(result);
                }

                cachedQuery = query.Trim();
                CachedResults.Clear();
                CachedResults.AddRange(Results);

                StatusText =
                    Results.Count == 0
                        ? "No results found."
                        : $"{Results.Count} results";

                cachedStatusText = StatusText;
            }
            catch (TaskCanceledException)
            {
            }
            catch (System.Exception)
            {
                Results.Clear();
                StatusText = "Search failed. Please try again.";
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    IsSearching = false;
                    LoadingVisibility = Visibility.Collapsed;
                    Bindings.Update();
                }
            }
        }

        private void Result_Tapped(
            object sender,
            TappedRoutedEventArgs e)
        {
            if (sender is not Border result ||
                result.DataContext is not SearchResultItem item)
            {
                return;
            }

            if (item.Kind == "Channel" &&
                item.Channel != null)
            {
                Frame.Navigate(typeof(ChannelPage), item.Channel);
                return;
            }

            if (item.Video != null)
            {
                Frame.Navigate(
                    typeof(VideoPage),
                    new VideoNavigationData
                    {
                        CurrentVideo = item.Video,
                        AllVideos = Results
                            .Where(resultItem => resultItem.Video != null)
                            .Select(resultItem => resultItem.Video!)
                            .ToList()
                    });
            }
        }

        private void ThumbnailImage_ImageFailed(
            object sender,
            ExceptionRoutedEventArgs e)
        {
            if (sender is not Image image ||
                image.DataContext is not SearchResultItem item ||
                string.IsNullOrWhiteSpace(item.FallbackThumbnail) ||
                item.FallbackThumbnail == item.Thumbnail ||
                !System.Uri.TryCreate(item.FallbackThumbnail, System.UriKind.Absolute, out System.Uri? fallbackUri))
            {
                return;
            }

            image.Source = new BitmapImage(fallbackUri)
            {
                DecodePixelWidth = 240,
                DecodePixelHeight = 136
            };
        }

    }
}