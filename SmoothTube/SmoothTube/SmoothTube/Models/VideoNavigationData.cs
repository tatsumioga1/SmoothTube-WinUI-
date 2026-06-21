using System.Collections.Generic;

namespace SmoothTube.Models
{
    public class VideoNavigationData
    {
        public VideoItem CurrentVideo { get; set; } = new();

        public List<VideoItem> AllVideos { get; set; } = [];
    }
}
