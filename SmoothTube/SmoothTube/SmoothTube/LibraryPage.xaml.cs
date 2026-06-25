using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SmoothTube.Models;
using SmoothTube.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmoothTube
{
    public sealed partial class LibraryPage : Page
    {
        private const string LibraryCacheFileName = "library-playlists-cache.json";

        private sealed class LibraryCache
        {
            public List<PlaylistItem> Playlists { get; set; } = [];
            public DateTimeOffset? RefreshedAt { get; set; }
        }

        private static readonly List<PlaylistItem> CachedPlaylists = [];

        private static DateTimeOffset? cachedPlaylistsRefreshedAt;

        private static bool triedPersistentLibraryCache;

        public ObservableCollection<VideoItem> ContinueWatchingVideos { get; } = [];

        public ObservableCollection<PlaylistItem> Playlists { get; } = [];

        public ObservableCollection<VideoItem> PlaylistVideos { get; } = [];

        public string StatusText { get; set; } =
            "Sign in from Settings to load your YouTube playlists.";

        public string EmptyPlaylistsText { get; set; } =
            "No owned YouTube playlists were returned for this account. Saved playlists from YouTube Library are not exposed by the YouTube Data API, so this section can only show playlists owned by the signed-in channel.";

        public string SelectedPlaylistTitle { get; set; } = "";

        public Visibility LoadingVisibility { get; set; } = Visibility.Collapsed;

        public Visibility ContinueWatchingVisibility { get; set; } = Visibility.Collapsed;

        public Visibility PlaylistsVisibility { get; set; } = Visibility.Visible;

        public Visibility EmptyPlaylistsVisibility { get; set; } = Visibility.Collapsed;

        public Visibility PlaylistVideosVisibility { get; set; } = Visibility.Collapsed;

        public Visibility PlaylistVideosLoadingVisibility { get; set; } = Visibility.Collapsed;

        public bool IsLoading { get; set; }

        public bool IsLoadingPlaylistVideos { get; set; }

        private CancellationTokenSource? loadCancellation;

        private bool hasLoaded;

        public LibraryPage()
        {
            InitializeComponent();

            Loaded += LibraryPage_Loaded;
        }

        private async void LibraryPage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            LoadContinueWatching();

            if (hasLoaded)
                return;

            hasLoaded = true;
            await LoadPlaylistsAsync(false);
        }

        private async void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadContinueWatching();
            await LoadPlaylistsAsync(true);
        }

        private void LoadContinueWatching()
        {
            ContinueWatchingVideos.Clear();

            foreach (VideoItem video in WatchHistoryService.GetContinueWatching())
            {
                ContinueWatchingVideos.Add(video);
            }

            ContinueWatchingVisibility =
                ContinueWatchingVideos.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            Bindings.Update();
        }

        private async Task LoadPlaylistsAsync(bool forceRefresh)
        {
            loadCancellation?.Cancel();
            loadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = loadCancellation.Token;

            if (!ServiceLocator.GoogleOAuth.IsSignedIn)
            {
                StatusText =
                    "Sign in from Settings to load your YouTube playlists.";

                Playlists.Clear();
                PlaylistVideos.Clear();
                EmptyPlaylistsVisibility = Visibility.Collapsed;
                PlaylistVideosVisibility = Visibility.Collapsed;
                LoadingVisibility = Visibility.Collapsed;
                PlaylistsVisibility = Visibility.Visible;
                Bindings.Update();
                return;
            }

            if (!forceRefresh)
            {
                EnsurePersistentLibraryCacheLoaded();

                if (CachedPlaylists.Count > 0)
                {
                    ReplacePlaylists(CachedPlaylists);
                    EmptyPlaylistsVisibility = Visibility.Collapsed;
                    StatusText = FormatPlaylistStatus();
                    LoadingVisibility = Visibility.Collapsed;
                    PlaylistsVisibility = Visibility.Visible;
                    Bindings.Update();
                    return;
                }
            }

            IsLoading = true;
            LoadingVisibility = Visibility.Visible;
            PlaylistsVisibility = Visibility.Collapsed;
            EmptyPlaylistsVisibility = Visibility.Collapsed;
            StatusText = forceRefresh
                ? "Refreshing your YouTube playlists..."
                : "Loading your YouTube playlists...";

            Bindings.Update();

            try
            {
                List<PlaylistItem> playlists =
                    await ServiceLocator.YouTube.GetPlaylistsAsync(cancellationToken);

                ReplacePlaylists(playlists);
                SavePlaylistsToCache(playlists);

                EmptyPlaylistsVisibility =
                    Playlists.Count == 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                StatusText =
                    Playlists.Count == 0
                        ? "No owned YouTube playlists were returned for this account."
                        : FormatPlaylistStatus();
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                StatusText =
                    $"Could not load your YouTube playlists: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                LoadingVisibility = Visibility.Collapsed;
                PlaylistsVisibility = Visibility.Visible;
                Bindings.Update();
            }
        }

        private async void PlaylistCard_Tapped(
            object sender,
            TappedRoutedEventArgs e)
        {
            if (sender is not Border border ||
                border.DataContext is not PlaylistItem playlist)
            {
                return;
            }

            await LoadPlaylistVideosAsync(playlist);
        }

        private async Task LoadPlaylistVideosAsync(PlaylistItem playlist)
        {
            if (IsLoadingPlaylistVideos)
                return;

            loadCancellation?.Cancel();
            loadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = loadCancellation.Token;

            IsLoadingPlaylistVideos = true;
            PlaylistVideosLoadingVisibility = Visibility.Visible;
            PlaylistVideosVisibility = Visibility.Visible;
            SelectedPlaylistTitle = playlist.Title;
            PlaylistVideos.Clear();

            StatusText =
                $"Loading {playlist.Title}...";

            Bindings.Update();

            try
            {
                List<VideoItem> videos =
                    await ServiceLocator.YouTube.GetPlaylistVideosAsync(
                        playlist.Id,
                        cancellationToken);

                ReplaceVideos(videos);

                StatusText =
                    $"{playlist.Title}: {PlaylistVideos.Count} videos loaded.";
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                StatusText =
                    $"Could not load this playlist: {ex.Message}";
            }
            finally
            {
                IsLoadingPlaylistVideos = false;
                PlaylistVideosLoadingVisibility = Visibility.Collapsed;
                Bindings.Update();
            }
        }

        private void VideoCard_Tapped(
            object sender,
            TappedRoutedEventArgs e)
        {
            if (sender is Border card &&
                card.DataContext is VideoItem video)
            {
                List<VideoItem> navigationVideos =
                    PlaylistVideos.Count > 0 && PlaylistVideos.Contains(video)
                        ? PlaylistVideos.ToList()
                        : ContinueWatchingVideos.ToList();

                Frame.Navigate(
                    typeof(VideoPage),
                    new VideoNavigationData
                    {
                        CurrentVideo = video,
                        AllVideos = navigationVideos.Count > 0
                            ? navigationVideos
                            : [video]
                    });
            }
        }

        private void ReplacePlaylists(IEnumerable<PlaylistItem> playlists)
        {
            Playlists.Clear();

            foreach (PlaylistItem playlist in playlists)
            {
                Playlists.Add(playlist);
            }
        }

        private void ReplaceVideos(IEnumerable<VideoItem> videos)
        {
            PlaylistVideos.Clear();

            foreach (VideoItem video in videos)
            {
                PlaylistVideos.Add(video);
            }
        }

        private string FormatPlaylistStatus()
        {
            string cacheText =
                cachedPlaylistsRefreshedAt.HasValue
                    ? $" Last updated {cachedPlaylistsRefreshedAt.Value.LocalDateTime:g}."
                    : "";

            return
                $"{Playlists.Count} owned playlists loaded." +
                cacheText;
        }

        private static void EnsurePersistentLibraryCacheLoaded()
        {
            if (triedPersistentLibraryCache)
                return;

            triedPersistentLibraryCache = true;

            LibraryCache? cache =
                PersistentCacheService.Load<LibraryCache>(
                    LibraryCacheFileName);

            if (cache == null)
                return;

            CachedPlaylists.Clear();
            CachedPlaylists.AddRange(cache.Playlists ?? []);
            cachedPlaylistsRefreshedAt = cache.RefreshedAt;
        }

        private static void SavePlaylistsToCache(List<PlaylistItem> playlists)
        {
            CachedPlaylists.Clear();
            CachedPlaylists.AddRange(playlists);
            cachedPlaylistsRefreshedAt = DateTimeOffset.Now;

            PersistentCacheService.Save(
                LibraryCacheFileName,
                new LibraryCache
                {
                    Playlists = CachedPlaylists.ToList(),
                    RefreshedAt = cachedPlaylistsRefreshedAt
                });
        }
    }
}
