using SmoothTube.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Windows.Storage;

namespace SmoothTube.Services
{
    public static class WatchHistoryService
    {
        private const string ContinueWatchingFileName = "continue-watching.json";
        private const int MaxContinueWatchingItems = 12;
        private const double CompletedProgressThreshold = 95;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        public static void RecordStarted(VideoItem video)
        {
            if (video == null || string.IsNullOrWhiteSpace(video.Id))
            {
                return;
            }

            List<VideoItem> videos = GetContinueWatching();
            VideoItem? existing = videos.FirstOrDefault(item => item.Id == video.Id);

            videos.RemoveAll(item => item.Id == video.Id);

            VideoItem historyItem = CreateHistoryItem(video, existing);

            // Keep freshly opened videos in Continue Watching, but do not invent
            // progress. The progress bar should reflect a real player timestamp.
            historyItem.LastWatchedAt = DateTimeOffset.Now;

            videos.Insert(0, historyItem);
            Save(NormalizeHistory(videos));
        }

        public static void RecordProgress(
            VideoItem video,
            double currentSeconds,
            double durationSeconds)
        {
            if (video == null ||
                string.IsNullOrWhiteSpace(video.Id) ||
                video.IsLive ||
                video.IsPremiere)
            {
                return;
            }

            if (double.IsNaN(currentSeconds) ||
                double.IsInfinity(currentSeconds) ||
                currentSeconds < 0)
            {
                return;
            }

            List<VideoItem> videos = GetContinueWatching();
            VideoItem? existing = videos.FirstOrDefault(item => item.Id == video.Id);

            if (double.IsNaN(durationSeconds) ||
                double.IsInfinity(durationSeconds) ||
                durationSeconds < 0)
            {
                durationSeconds = 0;
            }

            durationSeconds = ResolveDurationSeconds(
                durationSeconds,
                video.DurationSeconds,
                existing?.DurationSeconds ?? 0,
                video.Duration,
                existing?.Duration ?? "");

            double progress =
                durationSeconds > 0
                    ? Math.Clamp(currentSeconds / durationSeconds * 100, 0, 100)
                    : ResolveFallbackProgress(currentSeconds, video.Progress, existing?.Progress ?? 0);

            video.ResumeSeconds = Math.Max(0, currentSeconds);
            video.DurationSeconds = Math.Max(video.DurationSeconds, durationSeconds);
            video.Progress = progress;
            video.LastWatchedAt = DateTimeOffset.Now;

            if (progress >= CompletedProgressThreshold)
            {
                Remove(video.Id);
                return;
            }

            videos.RemoveAll(item => item.Id == video.Id);

            VideoItem historyItem = CreateHistoryItem(video, existing);
            historyItem.ResumeSeconds = video.ResumeSeconds;
            historyItem.DurationSeconds = video.DurationSeconds;
            historyItem.Progress = progress;
            historyItem.LastWatchedAt = DateTimeOffset.Now;

            videos.Insert(0, historyItem);
            Save(NormalizeHistory(videos));
        }

        public static void RecordCompleted(VideoItem video)
        {
            if (video == null || string.IsNullOrWhiteSpace(video.Id))
            {
                return;
            }

            Remove(video.Id);
        }

        public static void UpdateMetadata(VideoItem video)
        {
            if (video == null || string.IsNullOrWhiteSpace(video.Id))
            {
                return;
            }

            List<VideoItem> videos = GetContinueWatching();
            int index = videos.FindIndex(item => item.Id == video.Id);

            if (index < 0)
            {
                return;
            }

            VideoItem existing = videos[index];
            videos[index] = CreateHistoryItem(video, existing);
            Save(NormalizeHistory(videos));
        }

        public static void ApplySavedProgress(VideoItem video)
        {
            if (video == null || string.IsNullOrWhiteSpace(video.Id))
            {
                return;
            }

            VideoItem? saved =
                GetContinueWatching()
                    .FirstOrDefault(item => item.Id == video.Id);

            if (saved == null)
            {
                return;
            }

            video.ResumeSeconds = saved.ResumeSeconds;
            video.DurationSeconds = saved.DurationSeconds;
            video.Progress = NormalizeProgress(saved.Progress);

            if (string.IsNullOrWhiteSpace(video.Duration))
            {
                video.Duration = saved.Duration;
            }

            if (string.IsNullOrWhiteSpace(video.Thumbnail))
            {
                video.Thumbnail = saved.Thumbnail;
            }
        }

        public static double GetResumeSeconds(string videoId)
        {
            if (string.IsNullOrWhiteSpace(videoId))
            {
                return 0;
            }

            return GetContinueWatching()
                .FirstOrDefault(video => video.Id == videoId)?
                .ResumeSeconds ?? 0;
        }

        public static List<VideoItem> GetContinueWatching()
        {
            try
            {
                string path = GetContinueWatchingFilePath();

                if (!File.Exists(path))
                {
                    return [];
                }

                string rawValue = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return [];
                }

                List<VideoItem> videos =
                    JsonSerializer.Deserialize<List<VideoItem>>(rawValue, JsonOptions) ?? [];

                return NormalizeHistory(videos);
            }
            catch
            {
                return [];
            }
        }

        public static void Clear()
        {
            try
            {
                string path = GetContinueWatchingFilePath();

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore clear failures so the app never crashes from history cleanup.
            }
        }

        private static void Remove(string videoId)
        {
            if (string.IsNullOrWhiteSpace(videoId))
            {
                return;
            }

            List<VideoItem> videos = GetContinueWatching();
            videos.RemoveAll(item => item.Id == videoId);
            Save(NormalizeHistory(videos));
        }

        private static VideoItem CreateHistoryItem(
            VideoItem source,
            VideoItem? existing = null)
        {
            double resumeSeconds =
                source.ResumeSeconds > 0
                    ? source.ResumeSeconds
                    : existing?.ResumeSeconds ?? 0;

            double durationSeconds = ResolveDurationSeconds(
                source.DurationSeconds,
                existing?.DurationSeconds ?? 0,
                0,
                source.Duration,
                existing?.Duration ?? "");

            double progress =
                source.Progress > 0
                    ? source.Progress
                    : durationSeconds > 0 && resumeSeconds > 0
                        ? Math.Clamp(resumeSeconds / durationSeconds * 100, 0, 100)
                        : existing?.Progress ?? 0;

            return new VideoItem
            {
                PublishedAtSort = source.PublishedAtSort ?? existing?.PublishedAtSort,
                Id = source.Id,
                Title = string.IsNullOrWhiteSpace(source.Title) ? existing?.Title ?? "" : source.Title,
                Channel = string.IsNullOrWhiteSpace(source.Channel) ? existing?.Channel ?? "" : source.Channel,
                Views = string.IsNullOrWhiteSpace(source.Views) ? existing?.Views ?? "" : source.Views,
                Duration = string.IsNullOrWhiteSpace(source.Duration) ? existing?.Duration ?? "" : source.Duration,
                PublishedAt = ResolvePublishedAtText(source, existing),
                Thumbnail = NormalizeVideoThumbnailUrl(
                    string.IsNullOrWhiteSpace(source.Thumbnail)
                        ? existing?.Thumbnail ?? ""
                        : source.Thumbnail),
                Category = string.IsNullOrWhiteSpace(source.Category) ? existing?.Category ?? "YouTube" : source.Category,
                ChannelId = string.IsNullOrWhiteSpace(source.ChannelId) ? existing?.ChannelId ?? "" : source.ChannelId,
                IsEmbeddable = source.IsEmbeddable || existing?.IsEmbeddable == true,
                IsLive = source.IsLive,
                IsPremiere = source.IsPremiere,
                IsShort = source.IsShort || existing?.IsShort == true,
                Progress = NormalizeProgress(progress),
                ResumeSeconds = Math.Max(0, resumeSeconds),
                DurationSeconds = Math.Max(0, durationSeconds),
                LastWatchedAt = DateTimeOffset.Now
            };
        }

        private static List<VideoItem> NormalizeHistory(IEnumerable<VideoItem> videos)
        {
            return videos
                .Where(video => !string.IsNullOrWhiteSpace(video.Id))
                .GroupBy(video => video.Id)
                .Select(group => group.First())
                .Select(video =>
                {
                    video.Thumbnail = NormalizeVideoThumbnailUrl(video.Thumbnail);
                    video.PublishedAt = ResolvePublishedAtText(video, null);
                    video.ResumeSeconds = Math.Max(0, video.ResumeSeconds);
                    video.DurationSeconds = ResolveDurationSeconds(
                        video.DurationSeconds,
                        0,
                        0,
                        video.Duration,
                        "");

                    if (video.Progress <= 0 &&
                        video.ResumeSeconds > 0 &&
                        video.DurationSeconds > 0)
                    {
                        video.Progress = Math.Clamp(
                            video.ResumeSeconds / video.DurationSeconds * 100,
                            0,
                            100);
                    }
                    else if (video.Progress <= 0 && video.ResumeSeconds > 0)
                    {
                        video.Progress = ResolveFallbackProgress(
                            video.ResumeSeconds,
                            video.Progress,
                            0);
                    }

                    video.Progress = NormalizeProgress(video.Progress);
                    return video;
                })
                .Where(video => video.Progress < CompletedProgressThreshold)
                .OrderByDescending(video => video.LastWatchedAt ?? DateTimeOffset.MinValue)
                .Take(MaxContinueWatchingItems)
                .ToList();
        }

        private static string ResolvePublishedAtText(
            VideoItem source,
            VideoItem? existing)
        {
            DateTimeOffset? publishedAt =
                source.PublishedAtSort ??
                existing?.PublishedAtSort;

            if (!source.IsLive &&
                !source.IsPremiere &&
                publishedAt.HasValue)
            {
                return FormatRelativePublishedAt(
                    publishedAt.Value);
            }

            return string.IsNullOrWhiteSpace(source.PublishedAt)
                ? existing?.PublishedAt ?? ""
                : source.PublishedAt;
        }

        private static string FormatRelativePublishedAt(
            DateTimeOffset value)
        {
            DateTimeOffset publishedAt = value.ToLocalTime();
            DateTimeOffset now = DateTimeOffset.Now;

            if (publishedAt > now)
            {
                publishedAt = now;
            }

            TimeSpan age = now - publishedAt;

            if (age.TotalMinutes < 1)
                return "Just now";

            if (age.TotalHours < 1)
            {
                int minutes = Math.Max(1, (int)Math.Floor(age.TotalMinutes));
                return minutes == 1
                    ? "1 minute ago"
                    : $"{minutes} minutes ago";
            }

            if (age.TotalDays < 1)
            {
                int hours = Math.Max(1, (int)Math.Floor(age.TotalHours));
                return hours == 1
                    ? "1 hour ago"
                    : $"{hours} hours ago";
            }

            if (age.TotalDays < 7)
            {
                int days = Math.Max(1, (int)Math.Floor(age.TotalDays));
                return days == 1
                    ? "1 day ago"
                    : $"{days} days ago";
            }

            if (age.TotalDays < 30)
            {
                int weeks = Math.Max(1, (int)Math.Floor(age.TotalDays / 7));
                return weeks == 1
                    ? "1 week ago"
                    : $"{weeks} weeks ago";
            }

            if (age.TotalDays < 365)
            {
                int months = Math.Max(1, (int)Math.Floor(age.TotalDays / 30));
                return months == 1
                    ? "1 month ago"
                    : $"{months} months ago";
            }

            int years = Math.Max(1, (int)Math.Floor(age.TotalDays / 365));
            return years == 1
                ? "1 year ago"
                : $"{years} years ago";
        }

        private static void Save(List<VideoItem> videos)
        {
            try
            {
                string path = GetContinueWatchingFilePath();

                string folder = Path.GetDirectoryName(path) ?? ApplicationData.Current.LocalFolder.Path;

                Directory.CreateDirectory(folder);

                string json = JsonSerializer.Serialize(videos, JsonOptions);

                File.WriteAllText(path, json);
            }
            catch
            {
                // Do not crash the app if Continue Watching cannot be saved.
            }
        }

        private static string GetContinueWatchingFilePath()
        {
            return Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                ContinueWatchingFileName);
        }

        private static double ResolveDurationSeconds(
            double primary,
            double secondary,
            double tertiary,
            string primaryDurationText,
            string secondaryDurationText)
        {
            double[] candidates =
            [
                primary,
                secondary,
                tertiary,
                ParseDurationSeconds(primaryDurationText),
                ParseDurationSeconds(secondaryDurationText)
            ];

            foreach (double candidate in candidates)
            {
                if (!double.IsNaN(candidate) &&
                    !double.IsInfinity(candidate) &&
                    candidate > 0)
                {
                    return candidate;
                }
            }

            return 0;
        }

        private static double ResolveFallbackProgress(
            double currentSeconds,
            double videoProgress,
            double existingProgress)
        {
            double progress =
                videoProgress > 0
                    ? videoProgress
                    : existingProgress;

            if (progress > 0)
            {
                return NormalizeProgress(progress);
            }

            // If the player gives us a real resume timestamp but does not expose
            // duration, still show a small real progress bar instead of hiding it.
            // The actual resume point is saved separately in ResumeSeconds.
            return currentSeconds >= 5
                ? 1
                : 0;
        }

        private static double ParseDurationSeconds(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            string[] parts =
                value
                    .Trim()
                    .Split(':', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0 || parts.Length > 3)
            {
                return 0;
            }

            double totalSeconds = 0;

            foreach (string part in parts)
            {
                if (!int.TryParse(part, out int parsed))
                {
                    return 0;
                }

                totalSeconds = totalSeconds * 60 + parsed;
            }

            return totalSeconds;
        }

        private static double NormalizeProgress(double progress)
        {
            if (double.IsNaN(progress) || double.IsInfinity(progress))
            {
                return 0;
            }

            return Math.Clamp(progress, 0, 100);
        }

        private static string NormalizeVideoThumbnailUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !value.Contains("ytimg.com/vi", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            value = value
                .Replace(@"\u0026", "&", StringComparison.Ordinal)
                .Replace(@"\u003d", "=", StringComparison.Ordinal)
                .Replace(@"\/", "/", StringComparison.Ordinal)
                .Replace(@"\u002F", "/", StringComparison.Ordinal);

            return Regex.Replace(
                value,
                @"/(?:default|mqdefault|hqdefault|sddefault)\.jpg",
                "/hq720.jpg",
                RegexOptions.IgnoreCase);
        }
    }
}
