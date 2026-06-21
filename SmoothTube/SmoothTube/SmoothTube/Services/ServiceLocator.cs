namespace SmoothTube.Services
{
    public static class ServiceLocator
    {
        public static IYouTubeService YouTube { get; } = new YouTubeService();

        public static GoogleOAuthService GoogleOAuth { get; } = new();
    }
}
