using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.System;

namespace SmoothTube.Services
{
    public sealed class GoogleOAuthService
    {
        private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

        private static readonly HttpClient HttpClient = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly string[] Scopes =
        [
            "https://www.googleapis.com/auth/youtube.readonly",
            "https://www.googleapis.com/auth/youtube.force-ssl"
        ];

        public bool IsSignedIn =>
            !string.IsNullOrWhiteSpace(AppSettings.GoogleAccessToken) ||
            !string.IsNullOrWhiteSpace(AppSettings.GoogleRefreshToken);

        public string LastError { get; private set; } = "";

        public async Task<bool> SignInAsync()
        {
            LastError = "";

            if (string.IsNullOrWhiteSpace(AppSettings.GoogleOAuthClientId))
            {
                LastError = "Missing OAuth client ID.";
                return false;
            }

            string state = CreateUrlSafeToken(32);
            string codeVerifier = CreateUrlSafeToken(64);
            string codeChallenge = CreateCodeChallenge(codeVerifier);

            using var listener = new HttpListener();
            int port = RandomNumberGenerator.GetInt32(49152, 65500);
            string redirectUri = $"http://127.0.0.1:{port}/";

            listener.Prefixes.Add(redirectUri);
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                LastError = $"Could not start local sign-in listener: {ex.Message}";
                return false;
            }

            string authorizationUrl =
                AuthorizationEndpoint +
                "?response_type=code" +
                $"&client_id={Uri.EscapeDataString(AppSettings.GoogleOAuthClientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                $"&scope={Uri.EscapeDataString(string.Join(' ', Scopes))}" +
                $"&state={Uri.EscapeDataString(state)}" +
                "&access_type=offline" +
                "&prompt=consent" +
                $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                "&code_challenge_method=S256";

            await Launcher.LaunchUriAsync(new Uri(authorizationUrl));

            HttpListenerContext context =
                await listener.GetContextAsync();

            string? error =
                context.Request.QueryString["error"];

            string? code =
                context.Request.QueryString["code"];

            string? returnedState =
                context.Request.QueryString["state"];

            await WriteBrowserResponseAsync(
                context.Response,
                !string.IsNullOrWhiteSpace(error) ||
                string.IsNullOrWhiteSpace(code) ||
                returnedState != state
                    ? "SmoothTube sign-in failed. You can close this tab."
                    : "SmoothTube sign-in complete. You can close this tab.");

            listener.Stop();

            if (!string.IsNullOrWhiteSpace(error))
            {
                LastError = $"Google returned: {error}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(code) || returnedState != state)
            {
                LastError = "Google did not return a valid authorization code.";
                return false;
            }

            return await ExchangeCodeAsync(
                code,
                codeVerifier,
                redirectUri);
        }

        public void SignOut()
        {
            AppSettings.GoogleAccessToken = "";
            AppSettings.GoogleRefreshToken = "";
            AppSettings.GoogleTokenExpiresAt = DateTimeOffset.MinValue;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            if (!string.IsNullOrWhiteSpace(AppSettings.GoogleAccessToken) &&
                AppSettings.GoogleTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return AppSettings.GoogleAccessToken;
            }

            if (string.IsNullOrWhiteSpace(AppSettings.GoogleRefreshToken))
                return "";

            List<KeyValuePair<string, string>> fields =
            [
                new KeyValuePair<string, string>("client_id", AppSettings.GoogleOAuthClientId),
                new KeyValuePair<string, string>("refresh_token", AppSettings.GoogleRefreshToken),
                new KeyValuePair<string, string>("grant_type", "refresh_token")
            ];

            AddClientSecretIfAvailable(fields);

            using var content = new FormUrlEncodedContent(fields);

            using HttpResponseMessage response =
                await HttpClient.PostAsync(TokenEndpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                LastError =
                    await ReadGoogleErrorAsync(response);

                return "";
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync();

            GoogleTokenResponse? tokenResponse =
                await JsonSerializer.DeserializeAsync<GoogleTokenResponse>(
                    stream,
                    JsonOptions);

            if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
                return "";

            SaveTokenResponse(tokenResponse);
            return AppSettings.GoogleAccessToken;
        }

        private static async Task<bool> ExchangeCodeAsync(
            string code,
            string codeVerifier,
            string redirectUri)
        {
            List<KeyValuePair<string, string>> fields =
            [
                new KeyValuePair<string, string>("client_id", AppSettings.GoogleOAuthClientId),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("code_verifier", codeVerifier),
                new KeyValuePair<string, string>("redirect_uri", redirectUri),
                new KeyValuePair<string, string>("grant_type", "authorization_code")
            ];

            AddClientSecretIfAvailable(fields);

            using var content = new FormUrlEncodedContent(fields);

            using HttpResponseMessage response =
                await HttpClient.PostAsync(TokenEndpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                ServiceLocator.GoogleOAuth.LastError =
                    await ReadGoogleErrorAsync(response);

                return false;
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync();

            GoogleTokenResponse? tokenResponse =
                await JsonSerializer.DeserializeAsync<GoogleTokenResponse>(
                    stream,
                    JsonOptions);

            if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
            {
                ServiceLocator.GoogleOAuth.LastError =
                    "Google token response did not contain an access token.";

                return false;
            }

            SaveTokenResponse(tokenResponse);
            return true;
        }

        private static void AddClientSecretIfAvailable(
            List<KeyValuePair<string, string>> fields)
        {
            if (string.IsNullOrWhiteSpace(AppSettings.GoogleOAuthClientSecret))
                return;

            fields.Add(
                new KeyValuePair<string, string>(
                    "client_secret",
                    AppSettings.GoogleOAuthClientSecret));
        }

        private static async Task<string> ReadGoogleErrorAsync(
            HttpResponseMessage response)
        {
            string body =
                await response.Content.ReadAsStringAsync();

            try
            {
                GoogleErrorResponse? errorResponse =
                    JsonSerializer.Deserialize<GoogleErrorResponse>(
                        body,
                        JsonOptions);

                return !string.IsNullOrWhiteSpace(errorResponse?.ErrorDescription)
                    ? errorResponse.ErrorDescription
                    : !string.IsNullOrWhiteSpace(errorResponse?.Error)
                        ? errorResponse.Error
                        : $"Google token request failed: {(int)response.StatusCode}";
            }
            catch (JsonException)
            {
                return $"Google token request failed: {(int)response.StatusCode}";
            }
        }

        private static void SaveTokenResponse(GoogleTokenResponse tokenResponse)
        {
            AppSettings.GoogleAccessToken =
                tokenResponse.AccessToken ?? "";

            if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
            {
                AppSettings.GoogleRefreshToken =
                    tokenResponse.RefreshToken;
            }

            AppSettings.GoogleTokenExpiresAt =
                DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        }

        private static async Task WriteBrowserResponseAsync(
            HttpListenerResponse response,
            string message)
        {
            byte[] body =
                Encoding.UTF8.GetBytes(
                    $"<!doctype html><title>SmoothTube</title><body style=\"font-family:Segoe UI,sans-serif;background:#111;color:#fff;padding:40px\"><h1>{WebUtility.HtmlEncode(message)}</h1></body>");

            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = body.Length;

            await response.OutputStream.WriteAsync(body);
            response.OutputStream.Close();
        }

        private static string CreateCodeChallenge(string codeVerifier)
        {
            byte[] hash =
                SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));

            return Base64UrlEncode(hash);
        }

        private static string CreateUrlSafeToken(int byteCount)
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(byteCount);
            return Base64UrlEncode(bytes);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert
                .ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private sealed class GoogleTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; set; }

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }

        private sealed class GoogleErrorResponse
        {
            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("error_description")]
            public string? ErrorDescription { get; set; }
        }
    }
}
