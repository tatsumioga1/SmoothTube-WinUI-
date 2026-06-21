using SmoothTube.Models;
using System.Collections.Generic;
using System.Linq;

namespace SmoothTube.Services
{
    public static class VideoCatalog
    {
        private static readonly List<VideoItem> Videos =
        [
            new VideoItem
            {
                Id = "frieren-episode-1",
                Title = "Frieren Episode 1",
                Description = "The journey of Frieren, an elf mage, continues after the defeat of the Demon King.",
                Channel = "Ani-One Asia",
                Views = "1.2M views",
                Duration = "24:00",
                PublishedAt = "Jan 5, 2026",
                Thumbnail = "Assets/frieren.jpg",
                Progress = 85,
                Category = "Anime"
            },
            new VideoItem
            {
                Id = "apothecary-diaries",
                Title = "Apothecary Diaries",
                Description = "Maomao, a young apothecary, navigates the intrigues of the imperial court with her knowledge of medicine.",
                Channel = "Muse Asia",
                Views = "800K views",
                Duration = "23:42",
                PublishedAt = "Jan 8, 2026",
                Thumbnail = "Assets/apothecary.jpg",
                Progress = 42,
                Category = "Anime"
            },
            new VideoItem
            {
                Id = "solo-leveling",
                Title = "Solo Leveling",
                Description = "Follow the journey of Sung Jin-Woo, a weak hunter who becomes the strongest after a mysterious event.",
                Channel = "Crunchyroll",
                Views = "4.8M views",
                Duration = "24:15",
                PublishedAt = "Jan 12, 2026",
                Thumbnail = "Assets/solo.jpg",
                Progress = 67,
                Category = "Anime"
            },
            new VideoItem
            {
                Id = "demon-slayer",
                Title = "Demon Slayer",
                Description = "High-action anime highlights and reactions from the latest arc.",
                Channel = "Crunchyroll",
                Views = "8.3M views",
                Duration = "18:20",
                PublishedAt = "Jan 14, 2026",
                Thumbnail = "Assets/frieren.jpg",
                Category = "Anime"
            },
            new VideoItem
            {
                Id = "minecraft-survival",
                Title = "Minecraft Survival",
                Description = "A relaxed survival run with base building, exploration, and resource gathering.",
                Channel = "Gaming Hub",
                Views = "2.4M views",
                Duration = "32:10",
                PublishedAt = "Jan 18, 2026",
                Thumbnail = "Assets/apothecary.jpg",
                Category = "Gaming"
            },
            new VideoItem
            {
                Id = "yoasobi-live",
                Title = "YOASOBI Live",
                Description = "A concert performance with live band arrangements and stage visuals.",
                Channel = "YOASOBI",
                Views = "5.5M views",
                Duration = "41:05",
                PublishedAt = "Jan 21, 2026",
                Thumbnail = "Assets/solo.jpg",
                Category = "Music"
            }
        ];

        public static List<VideoItem> GetAll()
        {
            return Videos.ToList();
        }

        public static List<VideoItem> GetContinueWatching()
        {
            return Videos
                .Where(video => video.Progress > 0)
                .ToList();
        }

        public static List<VideoItem> GetByCategory(string category)
        {
            return Videos
                .Where(video => video.Category == category)
                .ToList();
        }

        public static List<VideoItem> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return GetAll();

            string normalizedQuery = query.Trim().ToLowerInvariant();

            return Videos
                .Where(video =>
                    video.Title.ToLowerInvariant().Contains(normalizedQuery) ||
                    video.Channel.ToLowerInvariant().Contains(normalizedQuery) ||
                    video.Category.ToLowerInvariant().Contains(normalizedQuery))
                .ToList();
        }
    }
}
