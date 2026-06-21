using System;
using Windows.Storage;

namespace SmoothTube.Services
{
    public static class AppSettings
    {
        private const string YouTubeApiKeySetting = "YouTubeApiKey";
        private const string GoogleOAuthClientIdSetting = "GoogleOAuthClientId";
        private const string GoogleOAuthClientSecretSetting = "GoogleOAuthClientSecret";
        private const string GoogleAccessTokenSetting = "GoogleAccessToken";
        private const string GoogleRefreshTokenSetting = "GoogleRefreshToken";
        private const string GoogleTokenExpiresAtSetting = "GoogleTokenExpiresAt";

        public static string YouTubeApiKey
        {
            get
            {
                object? value =
                    ApplicationData.Current.LocalSettings.Values[YouTubeApiKeySetting];

                return value as string ?? "";
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values[YouTubeApiKeySetting] =
                    value.Trim();
            }
        }

        public static bool HasYouTubeApiKey =>
            !string.IsNullOrWhiteSpace(YouTubeApiKey);

        public static string GoogleOAuthClientId
        {
            get
            {
                object? value =
                    ApplicationData.Current.LocalSettings.Values[GoogleOAuthClientIdSetting];

                return value as string ?? "";
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values[GoogleOAuthClientIdSetting] =
                    value.Trim();
            }
        }

        public static string GoogleAccessToken
        {
            get => GetString(GoogleAccessTokenSetting);
            set => SetString(GoogleAccessTokenSetting, value);
        }

        public static string GoogleOAuthClientSecret
        {
            get => GetString(GoogleOAuthClientSecretSetting);
            set => SetString(GoogleOAuthClientSecretSetting, value);
        }

        public static string GoogleRefreshToken
        {
            get => GetString(GoogleRefreshTokenSetting);
            set => SetString(GoogleRefreshTokenSetting, value);
        }

        public static DateTimeOffset GoogleTokenExpiresAt
        {
            get
            {
                object? value =
                    ApplicationData.Current.LocalSettings.Values[GoogleTokenExpiresAtSetting];

                return value is string rawValue &&
                    DateTimeOffset.TryParse(rawValue, out DateTimeOffset expiresAt)
                        ? expiresAt
                        : DateTimeOffset.MinValue;
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values[GoogleTokenExpiresAtSetting] =
                    value.ToString("O");
            }
        }

        private static string GetString(string settingName)
        {
            object? value =
                ApplicationData.Current.LocalSettings.Values[settingName];

            return value as string ?? "";
        }

        private static void SetString(
            string settingName,
            string value)
        {
            ApplicationData.Current.LocalSettings.Values[settingName] =
                value.Trim();
        }
    }
}
