namespace SmoothTube.Models
{
    public class PlaylistItem
    {
        public string Id { get; set; } = "";

        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public string Thumbnail { get; set; } = "";

        public int VideoCount { get; set; }

        public string PrivacyStatus { get; set; } = "";

        public string VideoCountText =>
            VideoCount == 1
                ? "1 video"
                : $"{VideoCount} videos";
    }
}
