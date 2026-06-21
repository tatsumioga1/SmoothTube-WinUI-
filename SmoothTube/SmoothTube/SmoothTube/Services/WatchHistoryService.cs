using SmoothTube.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Storage;

namespace SmoothTube.Services
{
    public static class WatchHistoryService
    {
        private const string ContinueWatchingSetting = "ContinueWatchingVideos";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static void RecordStarted(VideoItem video)
        {
            if (string.IsNullOrWhiteSpace(video.Id))
                return;

            List<VideoItem> videos = GetContinueWatching();

            videos.RemoveAll(item => item.Id == video.Id);

            videos.Insert(
                0,
                new VideoItem
                {
                    Id = video.Id,
                    Title = video.Title,
                    Channel = video.Channel,
                    Views = video.Views,
                    Duration = video.Duration,
                    PublishedAt = video.PublishedAt,
                    Thumbnail = NormalizeVideoThumbnailUrl(video.Thumbnail),
                    Description = video.Description,
                    Category = video.Category,
                    ChannelId = video.ChannelId,
                    IsEmbeddable = video.IsEmbeddable,
                    IsLive = video.IsLive,
                    IsPremiere = video.IsPremiere,
                    IsShort = video.IsShort,
                    Likes = video.Likes,
                    LiveChatId = video.LiveChatId,
                    Progress = NormalizeProgress(video.Progress)
                });

            Save(videos.Take(3).ToList());
        }

        public static List<VideoItem> GetContinueWatching()
        {
            object? value =
                ApplicationData.Current.LocalSettings.Values[ContinueWatchingSetting];

            if (value is not string rawValue ||
                string.IsNullOrWhiteSpace(rawValue))
            {
                return [];
            }

            try
            {
                List<VideoItem> videos =
                    JsonSerializer.Deserialize<List<VideoItem>>(
                    rawValue,
                    JsonOptions) ?? [];

                foreach (VideoItem video in videos)
                {
                    video.Thumbnail =
                        NormalizeVideoThumbnailUrl(video.Thumbnail);
                }

                return videos;
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static double NormalizeProgress(double progress)
        {
            if (progress is > 0 and < 95)
                return progress;

            return 35;
        }

        private static void Save(List<VideoItem> videos)
        {
            ApplicationData.Current.LocalSettings.Values[ContinueWatchingSetting] =
                JsonSerializer.Serialize(videos, JsonOptions);
        }

        private static string NormalizeVideoThumbnailUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !value.Contains("ytimg.com/vi/", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            return Regex.Replace(
                value,
                @"/(?:default|mqdefault|hqdefault|sddefault)\.jpg",
                "/hq720.jpg",
                RegexOptions.IgnoreCase);
        }
    }
}
