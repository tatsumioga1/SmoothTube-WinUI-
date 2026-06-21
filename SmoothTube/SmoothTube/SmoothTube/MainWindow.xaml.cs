using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using SmoothTube.Models;
using SmoothTube.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Windows.Storage;
using WinRT.Interop;

namespace SmoothTube
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }

        private readonly AppWindow appWindow;
        private readonly Dictionary<string, ChannelItem> subscriptionShortcuts = [];
        private const string CachedSubscriptionShortcutsFile = "subscription-shortcuts.json";
        private bool subscriptionShortcutsLoaded;
        private bool subscriptionShortcutsLoading;

        public MainWindow()
        {
            InitializeComponent();

            Instance = this;

            nint windowHandle = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
            appWindow = AppWindow.GetFromWindowId(windowId);

            // Windows 11 Mica
            SystemBackdrop = new MicaBackdrop
            {
                Kind = MicaKind.BaseAlt
            };

            // Extend Mica into title bar
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            RootGrid.Loaded += MainWindow_Loaded;
            ContentFrame.Navigated += ContentFrame_Navigated;
            LoadCachedSubscriptionShortcuts();
            ContentFrame.Navigate(typeof(HomePage));
        }

        public void SetFullScreen(bool isFullScreen)
        {
            if (isFullScreen)
            {
                AppTitleBar.Visibility = Visibility.Collapsed;
                Grid.SetRow(AppNavigation, 0);
                Grid.SetRowSpan(AppNavigation, 2);
                AppNavigation.IsPaneVisible = false;
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                return;
            }

            AppTitleBar.Visibility = Visibility.Visible;
            Grid.SetRow(AppNavigation, 1);
            Grid.SetRowSpan(AppNavigation, 1);
            AppNavigation.IsPaneVisible = true;
            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        }

        private void AppNavigation_SelectionChanged(
            NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                NavigateToPage(typeof(SettingsPage));
                return;
            }

            if (args.SelectedItemContainer == null)
                return;

            var tag = args.SelectedItemContainer.Tag?.ToString();

            NavigateToTag(tag);
        }

        private void AppNavigation_ItemInvoked(
            NavigationView sender,
            NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                NavigateToPage(typeof(SettingsPage));
                return;
            }

            if (args.InvokedItemContainer?.Tag is string tag)
            {
                NavigateToTag(tag);
            }
        }

        private async void AppNavigation_Expanding(
            NavigationView sender,
            NavigationViewItemExpandingEventArgs args)
        {
            if (args.ExpandingItemContainer == SubscriptionChannelsNavItem)
            {
                _ = LoadSubscriptionShortcutsAsync();
            }
        }

        private async void MainWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await LoadSubscriptionShortcutsAsync();
        }

        private async System.Threading.Tasks.Task LoadSubscriptionShortcutsAsync()
        {
            if (subscriptionShortcutsLoaded || subscriptionShortcutsLoading)
                return;

            if (!ServiceLocator.GoogleOAuth.IsSignedIn)
                return;

            subscriptionShortcutsLoading = true;

            List<ChannelItem> subscriptions =
                await ServiceLocator.YouTube.GetSubscriptionsAsync();

            ApplySubscriptionShortcuts(subscriptions);

            SaveCachedSubscriptionShortcuts(subscriptions);

            subscriptionShortcutsLoaded = true;
            subscriptionShortcutsLoading = false;
        }

        private void LoadCachedSubscriptionShortcuts()
        {
            string filePath =
                Path.Combine(
                    ApplicationData.Current.LocalFolder.Path,
                    CachedSubscriptionShortcutsFile);

            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                string rawValue = File.ReadAllText(filePath);

                List<CachedSubscriptionShortcut>? cachedShortcuts =
                    JsonSerializer.Deserialize<List<CachedSubscriptionShortcut>>(rawValue);

                if (cachedShortcuts?.Count > 0)
                {
                    ApplySubscriptionShortcuts(
                        cachedShortcuts
                            .Select(shortcut => new ChannelItem
                            {
                                Id = shortcut.Id,
                                Title = shortcut.Title
                            })
                            .ToList());

                    subscriptionShortcutsLoaded = true;
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        private static void SaveCachedSubscriptionShortcuts(List<ChannelItem> subscriptions)
        {
            List<CachedSubscriptionShortcut> shortcuts =
                subscriptions
                    .Select(channel => new CachedSubscriptionShortcut
                    {
                        Id = channel.Id,
                        Title = channel.Title
                    })
                    .Where(channel =>
                        !string.IsNullOrWhiteSpace(channel.Id) &&
                        !string.IsNullOrWhiteSpace(channel.Title))
                    .ToList();

            try
            {
                string filePath =
                    Path.Combine(
                        ApplicationData.Current.LocalFolder.Path,
                        CachedSubscriptionShortcutsFile);

                File.WriteAllText(
                    filePath,
                    JsonSerializer.Serialize(shortcuts));
            }
            catch (Exception ex) when (ex is IOException ||
                ex is UnauthorizedAccessException)
            {
            }
        }

        private void ApplySubscriptionShortcuts(List<ChannelItem> subscriptions)
        {
            SubscriptionChannelsNavItem.MenuItems.Clear();
            subscriptionShortcuts.Clear();

            foreach (ChannelItem channel in subscriptions)
            {
                string tag = $"Channel:{channel.Id}";
                subscriptionShortcuts[tag] = channel;

                SubscriptionChannelsNavItem.MenuItems.Add(
                    new NavigationViewItem
                    {
                        Content = channel.Title,
                        Tag = tag
                    });
            }

            SubscriptionChannelsNavItem.IsExpanded =
                SubscriptionChannelsNavItem.MenuItems.Count > 0;
        }

        private sealed class CachedSubscriptionShortcut
        {
            public string Id { get; set; } = "";

            public string Title { get; set; } = "";
        }

        private void ContentFrame_Navigated(
            object sender,
            Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            if (e.SourcePageType == typeof(VideoPage) ||
                e.SourcePageType == typeof(ChannelPage))
            {
                AppNavigation.SelectedItem = null;
            }
        }

        private void NavigateToTag(string? tag)
        {
            switch (tag)
            {
                case "Home":
                    NavigateToPage(typeof(HomePage));
                    break;

                case "Search":
                    NavigateToPage(typeof(SearchPage));
                    break;

                case "Library":
                    NavigateToPage(typeof(LibraryPage));
                    break;

                case "Subscriptions":
                    _ = LoadSubscriptionShortcutsAsync();
                    NavigateToPage(typeof(SubscriptionsPage));
                    break;

                case "SubscriptionChannels":
                    SubscriptionChannelsNavItem.IsExpanded = true;
                    break;

                case "Settings":
                    NavigateToPage(typeof(SettingsPage));
                    break;

                default:
                    if (tag != null &&
                        subscriptionShortcuts.TryGetValue(tag, out ChannelItem? channel))
                    {
                        ContentFrame.Navigate(typeof(ChannelPage), channel);
                    }
                    break;
            }
        }

        private void NavigateToPage(System.Type pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
