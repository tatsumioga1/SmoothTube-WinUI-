namespace SmoothTube.Models
{
    public class VideoItem
    {
        public string Id { get; set; } = "";

        public string Title { get; set; } = "";

        public string Channel { get; set; } = "";

        public string ChannelId { get; set; } = "";

        public string Views { get; set; } = "";

        public string Likes { get; set; } = "";

        public string Duration { get; set; } = "";

        public string PublishedAt { get; set; } = "";

        public System.DateTimeOffset? PublishedAtSort { get; set; }

        public bool IsLive { get; set; }

        public bool IsPremiere { get; set; }

        public bool IsShort { get; set; }

        public bool IsEmbeddable { get; set; }

        public string LiveChatId { get; set; } = "";

        public string Thumbnail { get; set; } = "";

        public string Description { get; set; } = "";

        public double Progress { get; set; }

        public string Category { get; set; } = "";
    }
}
