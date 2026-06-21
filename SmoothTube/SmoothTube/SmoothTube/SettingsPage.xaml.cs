using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SmoothTube.Services;

namespace SmoothTube
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();

            Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            ApiKeyBox.Password = AppSettings.YouTubeApiKey;
            OAuthClientIdBox.Text = AppSettings.GoogleOAuthClientId;
            OAuthClientSecretBox.Password = AppSettings.GoogleOAuthClientSecret;
            UpdateStatus();
        }

        private void SaveApiKey_Click(
            object sender,
            RoutedEventArgs e)
        {
            AppSettings.YouTubeApiKey = ApiKeyBox.Password;
            UpdateStatus();
        }

        private void ClearApiKey_Click(
            object sender,
            RoutedEventArgs e)
        {
            ApiKeyBox.Password = "";
            AppSettings.YouTubeApiKey = "";
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            ApiKeyStatusText.Text =
                string.IsNullOrWhiteSpace(AppSettings.YouTubeApiKey)
                    ? "Using local sample data."
                    : "API key saved. Search will use YouTube metadata when the network is available.";

            OAuthStatusText.Text =
                ServiceLocator.GoogleOAuth.IsSignedIn
                    ? "Signed in. SmoothTube can load subscriptions and account-backed YouTube data."
                    : "Not signed in. Add a Google OAuth desktop client ID, then sign in to enable subscriptions.";
        }

        private void SaveOAuthClientId_Click(
            object sender,
            RoutedEventArgs e)
        {
            AppSettings.GoogleOAuthClientId = OAuthClientIdBox.Text;
            AppSettings.GoogleOAuthClientSecret = OAuthClientSecretBox.Password;
            UpdateStatus();
        }

        private async void SignIn_Click(
            object sender,
            RoutedEventArgs e)
        {
            AppSettings.GoogleOAuthClientId = OAuthClientIdBox.Text;
            AppSettings.GoogleOAuthClientSecret = OAuthClientSecretBox.Password;

            bool signedIn =
                await ServiceLocator.GoogleOAuth.SignInAsync();

            OAuthStatusText.Text =
                signedIn
                    ? "Signed in. Subscriptions are now available."
                    : $"Sign-in did not complete. {ServiceLocator.GoogleOAuth.LastError}";
        }

        private void SignOut_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServiceLocator.GoogleOAuth.SignOut();
            UpdateStatus();
        }
    }
}
