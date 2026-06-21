using SmoothTube.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using Windows.Storage;

namespace SmoothTube.Services
{
    public sealed class YouTubeService : IYouTubeService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static List<ChannelItem>? cachedSubscriptions;
        private static List<VideoItem>? cachedSubscribedVideos;
        private static int cachedSubscribedVideosDays;
        private const string CachedSubscribedVideosFile = "subscription-videos.json";
        private const int CachedSubscribedVideosVersion = 9;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        static YouTubeService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SmoothTube/1.0 (+https://localhost)");
        }

        public async Task<List<VideoItem>> GetHomeVideosAsync(
            CancellationToken cancellationToken = default)
        {
            List<VideoItem> invidiousVideos =
                await GetInvidiousHomeVideosAsync(cancellationToken);

            if (invidiousVideos.Count > 0)
            {
                await TryEnrichVideosFromWatchPagesAsync(
                    invidiousVideos,
                    cancellationToken);

                return invidiousVideos;
            }

            if (!HasApiKey)
                return VideoCatalog.GetAll();

            List<VideoItem> videos =
                await SearchYouTubeAsync("popular videos", cancellationToken);

            await TryEnrichVideosFromWatchPagesAsync(videos, cancellationToken);
            return videos;
        }

        public async Task<List<VideoItem>> GetMoreHomeVideosAsync(
            IEnumerable<string> existingVideoIds,
            CancellationToken cancellationToken = default)
        {
            HashSet<string> existingIds =
                existingVideoIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<VideoItem> videos =
                await GetInvidiousHomeVideosAsync(
                    cancellationToken,
                    includeAdditionalFeeds: true);

            videos =
                videos
                    .Where(video => !existingIds.Contains(video.Id))
                    .GroupBy(video => video.Id)
                    .Select(group => group.First())
                    .Take(24)
                    .ToList();

            await TryEnrichVideosFromWatchPagesAsync(videos, cancellationToken);
            return videos;
        }

        public Task<List<VideoItem>> GetContinueWatchingAsync(
            CancellationToken cancellationToken = default)
        {
            if (HasApiKey)
                return Task.FromResult(new List<VideoItem>());

            return Task.FromResult(VideoCatalog.GetContinueWatching());
        }

        public async Task<List<VideoItem>> GetVideosByCategoryAsync(
            string category,
            CancellationToken cancellationToken = default)
        {
            if (!HasApiKey)
                return VideoCatalog.GetByCategory(category);

            return await SearchYouTubeAsync(category, cancellationToken);
        }

        public async Task<List<VideoItem>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return await GetHomeVideosAsync(cancellationToken);

            if (!HasApiKey && !ServiceLocator.GoogleOAuth.IsSignedIn)
                return VideoCatalog.Search(query);

            return await SearchYouTubeAsync(query, cancellationToken);
        }

        public async Task<List<SearchResultItem>> SearchAllAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return (await GetHomeVideosAsync(cancellationToken))
                    .Select(video => new SearchResultItem
                    {
                        Kind = "Video",
                        Video = video
                    })
                    .ToList();
            }

            Task<List<VideoItem>> videosTask =
                SearchAsync(query, cancellationToken);

            Task<List<ChannelItem>> channelsTask =
                HasApiKey || ServiceLocator.GoogleOAuth.IsSignedIn
                    ? SearchChannelsAsync(query, cancellationToken)
                    : Task.FromResult(new List<ChannelItem>());

            await Task.WhenAll(videosTask, channelsTask);

            List<SearchResultItem> results = [];

            results.AddRange(
                channelsTask.Result.Select(channel => new SearchResultItem
                {
                    Kind = "Channel",
                    Channel = channel
                }));

            results.AddRange(
                videosTask.Result.Select(video => new SearchResultItem
                {
                    Kind = "Video",
                    Video = video
                }));

            if (results.Count == 0)
            {
                results.AddRange(
                    (await SearchInvidiousAsync(query, cancellationToken))
                        .Select(video => new SearchResultItem
                        {
                            Kind = "Video",
                            Video = video
                        }));
            }

            if (results.Count == 0)
            {
                results.AddRange(
                    await SearchYouTubeResultsPageAsync(query, cancellationToken));
            }

            if (results.Count == 0)
            {
                results.AddRange(
                    VideoCatalog.Search(query)
                        .DefaultIfEmpty()
                        .Where(video => video != null)
                        .Select(video => new SearchResultItem
                        {
                            Kind = "Video",
                            Video = video
                        }));
            }

            return results;
        }

        public async Task<VideoItem?> GetVideoAsync(
            string videoId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(videoId))
                return null;

            var videos =
                new List<VideoItem>
                {
                    new()
                    {
                        Id = videoId,
                        Category = "YouTube",
                        IsEmbeddable = true
                    }
                };

            await TryEnrichVideosAsync(videos, cancellationToken);

            VideoItem video = videos[0];
            return string.IsNullOrWhiteSpace(video.Title)
                ? null
                : video;
        }

        public async Task<bool> RateVideoAsync(
            string videoId,
            string rating,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(videoId) ||
                string.IsNullOrWhiteSpace(rating))
            {
                return false;
            }

            string accessToken =
                await ServiceLocator.GoogleOAuth.GetAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(accessToken))
                return false;

            string requestUri =
                "https://www.googleapis.com/youtube/v3/videos/rate" +
                $"?id={Uri.EscapeDataString(videoId)}" +
                $"&rating={Uri.EscapeDataString(rating)}";

            using var request =
                new HttpRequestMessage(HttpMethod.Post, requestUri);

            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            using HttpResponseMessage response =
                await HttpClient.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode;
        }

        public async Task<string> GetVideoRatingAsync(
            string videoId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(videoId))
                return "";

            string accessToken =
                await ServiceLocator.GoogleOAuth.GetAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(accessToken))
                return "";

            string requestUri =
                "https://www.googleapis.com/youtube/v3/videos/getRating" +
                $"?id={Uri.EscapeDataString(videoId)}";

            try
            {
                using HttpResponseMessage response =
                    await SendAuthorizedGetAsync(
                        requestUri,
                        accessToken,
                        cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return "";

                await using var contentStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                using JsonDocument document =
                    await JsonDocument.ParseAsync(
                        contentStream,
                        cancellationToken: cancellationToken);

                if (!document.RootElement.TryGetProperty(
                        "items",
                        out JsonElement items) ||
                    items.ValueKind != JsonValueKind.Array)
                {
                    return "";
                }

                JsonElement firstItem =
                    items.EnumerateArray().FirstOrDefault();

                return firstItem.TryGetProperty(
                        "rating",
                        out JsonElement userRating)
                    ? userRating.GetString() ?? ""
                    : "";
            }
            catch (HttpRequestException)
            {
                return "";
            }
            catch (JsonException)
            {
                return "";
            }
            catch (TaskCanceledException)
            {
                return "";
            }
        }

        public async Task<List<CommentItem>> GetCommentsAsync(
            string videoId,
            CancellationToken cancellationToken = default)
        {
            if (!HasApiKey || string.IsNullOrWhiteSpace(videoId))
                return [];

            string encodedVideoId = Uri.EscapeDataString(videoId);
            string encodedKey = Uri.EscapeDataString(AppSettings.YouTubeApiKey);
            string requestUri =
                "https://www.googleapis.com/youtube/v3/commentThreads" +
                "?part=snippet&maxResults=30&order=relevance&textFormat=plainText" +
                $"&videoId={encodedVideoId}&key={encodedKey}";

            try
            {
                using HttpResponseMessage response =
                    await HttpClient.GetAsync(requestUri, cancellationToken);

                response.EnsureSuccessStatusCode();

                await using var contentStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                YouTubeCommentThreadsResponse? commentsResponse =
                    await JsonSerializer.DeserializeAsync<YouTubeCommentThreadsResponse>(
                        contentStream,
                        JsonOptions,
                        cancellationToken);

                return commentsResponse?.Items?
                    .Select(item => item.Snippet?.TopLevelComment?.Snippet)
                    .Where(snippet => snippet != null)
                    .Select(snippet => new CommentItem
                    {
                        Author = Decode(snippet?.AuthorDisplayName),
                        Text = Decode(snippet?.TextDisplay),
                        PublishedAt = FormatPublishedAt(snippet?.PublishedAt),
                        LikeCount = FormatLikeCount(snippet?.LikeCount)
                    })
                    .ToList() ?? [];
            }
            catch (HttpRequestException)
            {
                return [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public async Task<List<LiveChatMessageItem>> GetLiveChatMessagesAsync(
            string liveChatId,
            CancellationToken cancellationToken = default)
        {
            if (!HasApiKey || string.IsNullOrWhiteSpace(liveChatId))
                return [];

            string encodedLiveChatId = Uri.EscapeDataString(liveChatId);
            string encodedKey = Uri.EscapeDataString(AppSettings.YouTubeApiKey);
            string requestUri =
                "https://www.googleapis.com/youtube/v3/liveChat/messages" +
                "?part=snippet,authorDetails&maxResults=50" +
                $"&liveChatId={encodedLiveChatId}&key={encodedKey}";

            try
            {
                using HttpResponseMessage response =
                    await HttpClient.GetAsync(requestUri, cancellationToken);

                response.EnsureSuccessStatusCode();

                await using var contentStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                YouTubeLiveChatResponse? liveChatResponse =
                    await JsonSerializer.DeserializeAsync<YouTubeLiveChatResponse>(
                        contentStream,
                        JsonOptions,
                        cancellationToken);

                return liveChatResponse?.Items?
                    .Select(item => new LiveChatMessageItem
                    {
                        Author = Decode(item.AuthorDetails?.DisplayName),
                        Message = Decode(item.Snippet?.DisplayMessage),
                        PublishedAt = FormatPublishedAt(item.Snippet?.PublishedAt),
                        AuthorPhoto = item.AuthorDetails?.ProfileImageUrl ?? ""
                    })
                    .ToList() ?? [];
            }
            catch (HttpRequestException)
            {
                return [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static bool HasApiKey =>
            AppSettings.HasYouTubeApiKey;

        private static async Task<List<VideoItem>> SearchYouTubeAsync(
            string query,
            CancellationToken cancellationToken)
        {
            string encodedQuery = Uri.EscapeDataString(query);
            string requestUri =
                "https://www.googleapis.com/youtube/v3/search" +
                "?part=snippet&type=video&maxResults=24" +
                $"&q={encodedQuery}";

            try
            {
                using HttpResponseMessage response =
                    await SendYouTubeGetAsync(
                        requestUri,
                        cancellationToken);

                response.EnsureSuccessStatusCode();

                await using var contentStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                YouTubeSearchResponse? searchResponse =
                    await JsonSerializer.DeserializeAsync<YouTubeSearchResponse>(
                        contentStream,
                        JsonOptions,
                        cancellationToken);

                List<VideoItem> videos = searchResponse?.Items?
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id?.VideoId))
                    .Select(item => new VideoItem
                    {
                        Id = item.Id?.VideoId ?? "",
                        Title = Decode(item.Snippet?.Title),
                        Description = Decode(item.Snippet?.Description),
                        Channel = Decode(item.Snippet?.ChannelTitle),
                        ChannelId = item.Snippet?.ChannelId ?? "",
                        Views = "",
                        Duration = "",
                        PublishedAt = FormatPublishedAt(item.Snippet?.PublishedAt),
                        PublishedAtSort = item.Snippet?.PublishedAt,
                        IsLive = item.Snippet?.LiveBroadcastContent == "live",
                        IsEmbeddable = true,
                        Thumbnail = NormalizeVideoThumbnailUrl(
                            item.Snippet?.Thumbnails?.High?.Url ??
                            item.Snippet?.Thumbnails?.Medium?.Url ??
                            item.Snippet?.Thumbnails?.Default?.Url ??
                            ""),
                        Category = "YouTube"
                    })
                    .ToList() ?? [];

                await TryEnrichVideosAsync(videos, cancellationToken);

                return videos;
            }
            catch (HttpRequestException)
            {
                return VideoCatalog.Search(query);
            }
            catch (JsonException)
            {
                return VideoCatalog.Search(query);
            }
        }

        private static async Task TryEnrichVideosAsync(
            List<VideoItem> videos,
            CancellationToken cancellationToken)
        {
            try
            {
                foreach (VideoItem[] batch in videos.Chunk(50))
                {
                    await EnrichVideosAsync(batch.ToList(), cancellationToken);
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (JsonException)
            {
            }
            catch (TaskCanceledException)
            {
            }
        }

        public async Task<bool> SubscribeToChannelAsync(
            string channelId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return false;

            string accessToken =
                await ServiceLocator.GoogleOAuth.GetAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(accessToken))
                return false;

            string requestUri =
                "https://www.googleapis.com/youtube/v3/subscriptions?part=snippet";

            var payload = new
            {
                snippet = new
                {
                    resourceId = new
                    {
                        kind = "youtube#channel",
                        channelId
                    }
                }
            };

            using StringContent content =
                new(
                    JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8,
                    "application/json");

            using HttpRequestMessage request =
                new(HttpMethod.Post, requestUri);

            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            request.Content = content;

            try
            {
                using HttpResponseMessage response =
                    await HttpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    cachedSubscriptions = null;
                    return true;
                }

                return response.StatusCode == HttpStatusCode.Conflict;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        public async Task<bool> IsSubscribedToChannelAsync(
            string channelId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return false;

            List<ChannelItem> subscriptions =
                await GetSubscriptionsAsync(cancellationToken);

            return subscriptions.Any(channel =>
                string.Equals(
                    channel.Id,
                    channelId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static async Task TryEnrichVideosFromWatchPagesAsync(
            IEnumerable<VideoItem> videos,
            CancellationToken cancellationToken)
        {
            List<VideoItem> targets =
                videos
                    .Where(video => !string.IsNullOrWhiteSpace(video.Id))
                    .Where(video =>
                        string.IsNullOrWhiteSpace(video.Duration) ||
                        !video.IsShort ||
                        (!video.IsLive && !video.IsPremiere && LooksLikeBroadcast(video)))
                    .Take(120)
                    .ToList();

            if (targets.Count == 0)
                return;

            using SemaphoreSlim gate = new(6);

            Task[] tasks =
                targets
                    .Select(async video =>
                    {
                        await gate.WaitAsync(cancellationToken);

                        try
                        {
                            await EnrichVideoFromWatchPageAsync(video, cancellationToken);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    })
                    .ToArray();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (TaskCanceledException)
            {
            }
        }

        private static async Task EnrichVideoFromWatchPageAsync(
            VideoItem video,
            CancellationToken cancellationToken)
        {
            try
            {
                using HttpResponseMessage response =
                    await HttpClient.GetAsync(
                        "https://www.youtube.com/watch" +
                        $"?v={Uri.EscapeDataString(video.Id)}" +
                        "&bpctr=9999999999&has_verified=1",
                        cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return;

                string body =
                    await response.Content.ReadAsStringAsync(cancellationToken);

                string lengthSeconds =
                    MatchValue(
                        body,
                        @"""lengthSeconds"":""?(?<value>\d+)""?");

                if (string.IsNullOrWhiteSpace(video.Duration) &&
                    int.TryParse(
                        lengthSeconds,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int totalSeconds))
                {
                    video.Duration = FormatDuration(totalSeconds);
                }

                bool isPremiere =
                    body.Contains(
                        "upcomingEventData",
                        StringComparison.OrdinalIgnoreCase) ||
                    body.Contains(
                        @"""isUpcoming"":true",
                        StringComparison.OrdinalIgnoreCase) ||
                    body.Contains(
                        @"""liveBroadcastContent"":""upcoming""",
                        StringComparison.OrdinalIgnoreCase);

                bool isLive =
                    !isPremiere &&
                    (body.Contains(
                            @"""isLiveNow"":true",
                            StringComparison.OrdinalIgnoreCase) ||
                        body.Contains(
                            @"""isLive"":true",
                            StringComparison.OrdinalIgnoreCase) ||
                        body.Contains(
                            @"""isLiveContent"":true",
                            StringComparison.OrdinalIgnoreCase) ||
                        body.Contains(
                            @"""isLivePlayback"":true",
                            StringComparison.OrdinalIgnoreCase) ||
                        body.Contains(
                            @"""liveBroadcastContent"":""live""",
                            StringComparison.OrdinalIgnoreCase) ||
                        body.Contains(
                            "liveBroadcastDetails",
                            StringComparison.OrdinalIgnoreCase));

                if (isLive)
                {
                    video.IsLive = true;
                    video.IsPremiere = false;
                    video.Duration = "";
                    video.PublishedAtSort ??= DateTimeOffset.Now;
                }
                else if (isPremiere)
                {
                    video.IsPremiere = true;
                    video.IsLive = false;
                    video.PublishedAtSort ??= DateTimeOffset.Now;
                }

                if (body.Contains(
                        $"/shorts/{video.Id}",
                        StringComparison.OrdinalIgnoreCase) ||
                    body.Contains(
                        @"""isShortsEligible"":true",
                        StringComparison.OrdinalIgnoreCase) ||
                    body.Contains(
                        @"""shortsLockupViewModel""",
                        StringComparison.OrdinalIgnoreCase))
                {
                    video.IsShort = true;
                }

                if (string.IsNullOrWhiteSpace(video.Thumbnail))
                {
                    video.Thumbnail =
                        NormalizeVideoThumbnailUrl(
                            MatchBestYouTubeImageUrl(body, "i.ytimg.com"));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException ||
                ex is TaskCanceledException ||
                ex is RegexMatchTimeoutException)
            {
            }
        }

        private static async Task EnrichVideosAsync(
            List<VideoItem> videos,
            CancellationToken cancellationToken)
        {
            string videoIds =
                string.Join(",", videos.Select(video => video.Id));

            if (string.IsNullOrWhiteSpace(videoIds))
                return;

            string encodedIds = Uri.EscapeDataString(videoIds);
            string requestUri =
                "https://www.googleapis.com/youtube/v3/videos" +
                "?part=contentDetails,statistics,snippet,status,liveStreamingDetails" +
                $"&id={encodedIds}";

            using HttpResponseMessage response =
                await SendYouTubeGetAsync(
                    requestUri,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            await using var contentStream =
                await response.Content.ReadAsStreamAsync(cancellationToken);

            YouTubeVideosResponse? videosResponse =
                await JsonSerializer.DeserializeAsync<YouTubeVideosResponse>(
                    contentStream,
                    JsonOptions,
                    cancellationToken);

            Dictionary<string, YouTubeVideoDetails> detailsById =
                videosResponse?.Items?
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                    .ToDictionary(item => item.Id ?? "", item => item) ?? [];

            foreach (VideoItem video in videos)
            {
                if (!detailsById.TryGetValue(video.Id, out YouTubeVideoDetails? details))
                    continue;

                video.Title =
                    string.IsNullOrWhiteSpace(video.Title)
                        ? Decode(details.Snippet?.Title)
                        : video.Title;

                video.Duration =
                    FormatDuration(details.ContentDetails?.Duration);

                video.Views =
                    FormatViewCount(details.Statistics?.ViewCount);

                video.Likes =
                    FormatLikeCount(details.Statistics?.LikeCount);

                video.ChannelId =
                    details.Snippet?.ChannelId ?? video.ChannelId;

                video.Channel =
                    string.IsNullOrWhiteSpace(video.Channel)
                        ? Decode(details.Snippet?.ChannelTitle)
                        : video.Channel;

                video.PublishedAt =
                    FormatPublishedAt(details.Snippet?.PublishedAt);

                video.PublishedAtSort =
                    details.Snippet?.PublishedAt;

                video.Description =
                    string.IsNullOrWhiteSpace(video.Description)
                        ? Decode(details.Snippet?.Description)
                        : video.Description;

                video.Thumbnail =
                    string.IsNullOrWhiteSpace(video.Thumbnail)
                        ? NormalizeVideoThumbnailUrl(
                            details.Snippet?.Thumbnails?.High?.Url ??
                            details.Snippet?.Thumbnails?.Medium?.Url ??
                            details.Snippet?.Thumbnails?.Default?.Url ??
                            "")
                        : NormalizeVideoThumbnailUrl(video.Thumbnail);

                string liveBroadcastContent =
                    details.Snippet?.LiveBroadcastContent ?? "";

                video.IsLive =
                    video.IsLive ||
                    liveBroadcastContent == "live" ||
                    !string.IsNullOrWhiteSpace(details.LiveStreamingDetails?.ActiveLiveChatId);

                video.IsPremiere =
                    video.IsPremiere ||
                    !video.IsLive &&
                    liveBroadcastContent == "upcoming";

                video.LiveChatId =
                    details.LiveStreamingDetails?.ActiveLiveChatId ?? "";

                video.IsEmbeddable =
                    details.Status?.Embeddable != false;

                video.IsShort =
                    video.IsShort ||
                    IsShort(video);
            }
        }

        private static async Task<List<ChannelItem>> SearchChannelsAsync(
            string query,
            CancellationToken cancellationToken)
        {
            string requestUri =
                "https://www.googleapis.com/youtube/v3/search" +
                "?part=snippet&type=channel&maxResults=12" +
                $"&q={Uri.EscapeDataString(query)}";

            try
            {
                using HttpResponseMessage response =
                    await SendYouTubeGetAsync(
                        requestUri,
                        cancellationToken);

                response.EnsureSuccessStatusCode();

                await using var contentStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                YouTubeSearchResponse? searchResponse =
                    await JsonSerializer.DeserializeAsync<YouTubeSearchResponse>(
                        contentStream,
                        JsonOptions,
                        cancellationToken);

                return searchResponse?.Items?
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id?.ChannelId))
                    .Select(item => new ChannelItem
                    {
                        Id = item.Id?.ChannelId ?? "",
                        Title = Decode(item.Snippet?.Title),
                        Description = Decode(item.Snippet?.Description),
                        Thumbnail = NormalizeYouTubeImageUrl(
                            item.Snippet?.Thumbnails?.High?.Url ??
                            item.Snippet?.Thumbnails?.Medium?.Url ??
                            item.Snippet?.Thumbnails?.Default?.Url ??
                            "")
                    })
                    .ToList() ?? [];
            }
            catch (HttpRequestException)
            {
                return [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public async Task<List<ChannelItem>> GetSubscriptionsAsync(
            CancellationToken cancellationToken = default)
        {
            if (cachedSubscriptions != null)
                return cachedSubscriptions;

            string accessToken =
                await ServiceLocator.GoogleOAuth.GetAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(accessToken))
                return [];

            try
            {
                List<ChannelItem> subscriptions = [];
                string pageToken = "";

                do
                {
                    string requestUri =
                        "https://www.googleapis.com/youtube/v3/subscriptions" +
                        "?part=snippet&mine=true&maxResults=50&order=alphabetical" +
                        (string.IsNullOrWhiteSpace(pageToken)
                            ? ""
                            : $"&pageToken={Uri.EscapeDataString(pageToken)}");

                    using HttpResponseMessage response =
                        await SendAuthorizedGetAsync(
                            requestUri,
                            accessToken,
                            cancellationToken);

                    response.EnsureSuccessStatusCode();

                    await using var contentStream =
                        await response.Content.ReadAsStreamAsync(cancellationToken);

                    YouTubeSubscriptionsResponse? subscriptionsResponse =
                        await JsonSerializer.DeserializeAsync<YouTubeSubscriptionsResponse>(
                            contentStream,
                            JsonOptions,
                            cancellationToken);

                    subscriptions.AddRange(
                        subscriptionsResponse?.Items?
                            .Select(item => item.Snippet)
                            .Where(snippet => snippet != null)
                            .Select(snippet => new ChannelItem
                            {
                                Id = snippet?.ResourceId?.ChannelId ?? "",
                                Title = Decode(snippet?.Title),
                                Description = Decode(snippet?.Description),
                                PublishedAt = FormatPublishedAt(snippet?.PublishedAt),
                                Thumbnail =
                                    snippet?.Thumbnails?.Medium?.Url ??
                                    snippet?.Thumbnails?.Default?.Url ??
                                    ""
                            }) ?? []);

                    pageToken =
                        subscriptionsResponse?.NextPageToken ?? "";
                }
                while (!string.IsNullOrWhiteSpace(pageToken));

                cachedSubscriptions = subscriptions;
                return subscriptions;
            }
            catch (HttpRequestException)
            {
                return [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public async Task<List<VideoItem>> GetSubscribedVideosAsync(
            int maxAgeDays = 30,
            bool includeShorts = true,
            CancellationToken cancellationToken = default)
        {
            if (cachedSubscribedVideos != null &&
                cachedSubscribedVideosDays >= maxAgeDays)
            {
                return FilterSubscribedVideos(
                    cachedSubscribedVideos,
                    maxAgeDays,
                    includeShorts);
            }

            List<ChannelItem> subscriptions =
                await GetSubscriptionsAsync(cancellationToken);

            List<VideoItem> videos =
                await GetSubscribedVideosFromFeedsAsync(
                    subscriptions,
                    maxAgeDays,
                    cancellationToken);

            if (videos.Count == 0)
            {
                foreach (ChannelItem[] batch in subscriptions.Chunk(12))
                {
                    List<VideoItem>[] channelVideoGroups =
                        await Task.WhenAll(
                            batch.Select(channel =>
                                GetChannelVideosAsync(
                                    channel.Id,
                                    cancellationToken)));

                    videos.AddRange(
                        channelVideoGroups
                            .SelectMany(channelVideos => channelVideos));
                }
            }

            videos =
                videos
                    .Where(video => !string.IsNullOrWhiteSpace(video.Id))
                    .GroupBy(video => video.Id)
                    .Select(group => group.First())
                    .OrderByDescending(GetPublishedAtSort)
                    .ToList();

            await TryEnrichVideosAsync(videos.Take(150).ToList(), cancellationToken);
            await TryEnrichVideosFromWatchPagesAsync(videos.Take(120), cancellationToken);

            cachedSubscribedVideos = videos
                .Where(video => video.IsEmbeddable)
                .OrderByDescending(GetPublishedAtSort)
                .ToList();

            cachedSubscribedVideosDays = maxAgeDays;

            return FilterSubscribedVideos(
                cachedSubscribedVideos,
                maxAgeDays,
                includeShorts);
        }

        public async IAsyncEnumerable<List<VideoItem>> GetSubscribedVideoBatchesAsync(
            int maxAgeDays = 30,
            bool includeShorts = true,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (cachedSubscribedVideos != null &&
                cachedSubscribedVideosDays >= maxAgeDays)
            {
                yield return FilterSubscribedVideos(
                    cachedSubscribedVideos,
                    maxAgeDays,
                    includeShorts);

                yield break;
            }

            CachedSubscribedVideos? persistedCache =
                LoadCachedSubscribedVideos();

            if (persistedCache?.Videos.Count > 0 &&
                persistedCache.MaxAgeDays >= maxAgeDays)
            {
                cachedSubscribedVideos = persistedCache.Videos;
                cachedSubscribedVideosDays = persistedCache.MaxAgeDays;

                List<VideoItem> cachedVideos =
                    FilterSubscribedVideos(
                        cachedSubscribedVideos,
                        maxAgeDays,
                        includeShorts);

                if (cachedVideos.Count > 0)
                    yield return cachedVideos;
            }

            List<ChannelItem> subscriptions =
                await GetSubscriptionsAsync(cancellationToken);

            List<VideoItem> allVideos = [];

            foreach (ChannelItem[] batch in subscriptions.Chunk(8))
            {
                List<VideoItem>[] groups =
                    await Task.WhenAll(
                        batch.Select(channel =>
                            GetChannelSubscriptionVideosAsync(
                                channel.Id,
                                maxAgeDays,
                                cancellationToken)));

                List<VideoItem> batchVideos =
                    groups
                        .SelectMany(group => group)
                        .Where(video => video.IsEmbeddable)
                        .OrderByDescending(GetPublishedAtSort)
                        .ToList();

                await TryEnrichVideosAsync(
                    batchVideos
                        .Where(video => string.IsNullOrWhiteSpace(video.Duration))
                        .Take(100)
                        .ToList(),
                    cancellationToken);

                await TryEnrichVideosFromWatchPagesAsync(
                    batchVideos.Take(80),
                    cancellationToken);

                allVideos.AddRange(batchVideos);

                batchVideos =
                    FilterSubscribedVideos(
                        batchVideos,
                        maxAgeDays,
                        includeShorts);

                if (batchVideos.Count > 0)
                    yield return batchVideos;
            }

            cachedSubscribedVideos =
                allVideos
                    .Where(video => video.IsEmbeddable)
                    .OrderByDescending(GetPublishedAtSort)
                    .ToList();

            cachedSubscribedVideosDays = maxAgeDays;

            SaveCachedSubscribedVideos(
                cachedSubscribedVideos,
                maxAgeDays);
        }

        public async Task<List<VideoItem>> GetSubscribedBroadcastsAsync(
            CancellationToken cancellationToken = default)
        {
            List<ChannelItem> subscriptions =
                await GetSubscriptionsAsync(cancellationToken);

            List<VideoItem> broadcasts = [];

            foreach (ChannelItem[] batch in subscriptions.Chunk(8))
            {
                List<VideoItem>[] groups =
                    await Task.WhenAll(
                        batch.Select(channel =>
                            GetChannelBroadcastsForSubscriptionAsync(
                                channel.Id,
                                cancellationToken)));

                broadcasts.AddRange(groups.SelectMany(group => group));
            }

            List<VideoItem> distinctBroadcasts =
                broadcasts
                    .Where(video => !string.IsNullOrWhiteSpace(video.Id))
                    .GroupBy(video => video.Id)
                    .Select(group => group.First())
                    .ToList();

            await TryEnrichVideosAsync(distinctBroadcasts, cancellationToken);
            await TryEnrichVideosFromWatchPagesAsync(distinctBroadcasts, cancellationToken);

            return distinctBroadcasts
                .Where(video => video.IsEmbeddable)
                .OrderByDescending(video => video.IsLive)
                .ThenBy(video => video.IsPremiere)
                .ThenByDescending(GetPublishedAtSort)
                .ToList();
        }

        public void ClearSubscribedVideoCache()
        {
            cachedSubscribedVideos = null;
            cachedSubscribedVideosDays = 0;

            try
            {
                string filePath =
                    Path.Combine(
                        ApplicationData.Current.LocalFolder.Path,
                        CachedSubscribedVideosFile);

                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex) when (ex is IOException ||
                ex is UnauthorizedAccessException)
            {
            }
        }

        public async Task<ChannelItem?> GetChannelAsync(
            string channelId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return null;

            string requestUri =
                "https://www.googleapis.com/youtube/v3/channels" +
                "?part=snippet,brandingSettings,contentDetails" +
                $"&id={Uri.EscapeDataString(channelId)}";

            try
            {
                using HttpResponseMessage response =
                    await SendYouTubeGetAsync(
                        requestUri,
                        cancellationToken);

                response.EnsureSuccessStatusCode();

                await using var contentStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                YouTubeChannelsResponse? channelsResponse =
                    await JsonSerializer.DeserializeAsync<YouTubeChannelsResponse>(
                        contentStream,
                        JsonOptions,
                        cancellationToken);

                YouTubeChannel? channel =
                    channelsResponse?.Items?.FirstOrDefault();

                if (channel == null)
                    return null;

                return new ChannelItem
                {
                    Id = channel.Id ?? channelId,
                    Title = Decode(channel.Snippet?.Title),
                    Description = Decode(channel.Snippet?.Description),
                    Thumbnail =
                        channel.Snippet?.Thumbnails?.High?.Url ??
                        channel.Snippet?.Thumbnails?.Medium?.Url ??
                        channel.Snippet?.Thumbnails?.Default?.Url ??
                        "",
                    BannerImage =
                        FormatBannerUrl(channel.BrandingSettings?.Image?.BannerExternalUrl),
                    UploadsPlaylistId =
                        channel.ContentDetails?.RelatedPlaylists?.Uploads ?? ""
                };
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public async Task<List<VideoItem>> GetChannelVideosAsync(
            string channelId,
            CancellationToken cancellationToken = default)
        {
            return await GetChannelVideosAsync(
                channelId,
                24,
                cancellationToken);
        }

        public async Task<List<VideoItem>> GetChannelVideosAsync(
            string channelId,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return [];

            string uploadsPlaylistId =
                await GetUploadsPlaylistIdAsync(
                    channelId,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(uploadsPlaylistId))
                return [];

            try
            {
                List<VideoItem> videos = [];
                string? nextPageToken = null;

                do
                {
                    int pageSize =
                        Math.Min(50, Math.Max(1, maxResults - videos.Count));

                    string requestUri =
                        "https://www.googleapis.com/youtube/v3/playlistItems" +
                        $"?part=snippet&maxResults={pageSize}" +
                        $"&playlistId={Uri.EscapeDataString(uploadsPlaylistId)}";

                    if (!string.IsNullOrWhiteSpace(nextPageToken))
                    {
                        requestUri +=
                            $"&pageToken={Uri.EscapeDataString(nextPageToken)}";
                    }

                    using HttpResponseMessage response =
                        await SendYouTubeGetAsync(
                            requestUri,
                            cancellationToken);

                    response.EnsureSuccessStatusCode();

                    await using var contentStream =
                        await response.Content.ReadAsStreamAsync(cancellationToken);

                    YouTubePlaylistItemsResponse? playlistResponse =
                        await JsonSerializer.DeserializeAsync<YouTubePlaylistItemsResponse>(
                            contentStream,
                            JsonOptions,
                            cancellationToken);

                    videos.AddRange(
                        playlistResponse?.Items?
                        .Select(item => item.Snippet)
                        .Where(snippet => !string.IsNullOrWhiteSpace(snippet?.ResourceId?.VideoId))
                        .Select(snippet => new VideoItem
                        {
                            Id = snippet?.ResourceId?.VideoId ?? "",
                            Title = Decode(snippet?.Title),
                            Description = Decode(snippet?.Description),
                            Channel = Decode(snippet?.ChannelTitle),
                            ChannelId =
                                snippet?.VideoOwnerChannelId ??
                                snippet?.ChannelId ??
                                channelId,
                            PublishedAt = FormatPublishedAt(snippet?.PublishedAt),
                            PublishedAtSort = snippet?.PublishedAt,
                            Thumbnail = NormalizeVideoThumbnailUrl(
                                snippet?.Thumbnails?.High?.Url ??
                                snippet?.Thumbnails?.Medium?.Url ??
                                snippet?.Thumbnails?.Default?.Url ??
                                ""),
                            IsEmbeddable = true,
                            Category = "YouTube"
                        })
                        .ToList() ?? []);

                    nextPageToken = playlistResponse?.NextPageToken;
                }
                while (videos.Count < maxResults &&
                    !string.IsNullOrWhiteSpace(nextPageToken));

                await TryEnrichVideosAsync(videos, cancellationToken);
                await TryEnrichVideosFromWatchPagesAsync(
                    videos.Take(120),
                    cancellationToken);

                List<VideoItem> filteredVideos = videos
                    .Where(video => video.IsEmbeddable)
                    .ToList();

                if (filteredVideos.Count > 0)
                    return filteredVideos;

                return await GetChannelVideosFromFeedAsync(
                    channelId,
                    365,
                    cancellationToken);
            }
            catch (HttpRequestException)
            {
                return await GetChannelVideosFromFeedAsync(
                    channelId,
                    365,
                    cancellationToken);
            }
            catch (JsonException)
            {
                return await GetChannelVideosFromFeedAsync(
                    channelId,
                    365,
                    cancellationToken);
            }
        }

        private static async Task<List<VideoItem>> GetSubscribedVideosFromFeedsAsync(
            List<ChannelItem> subscriptions,
            int maxAgeDays,
            CancellationToken cancellationToken)
        {
            using var gate = new SemaphoreSlim(12);

            Task<List<VideoItem>>[] tasks =
                subscriptions
                    .Select(channel => GetChannelVideosFromFeedThrottledAsync(
                        channel.Id,
                        maxAgeDays,
                        gate,
                        cancellationToken))
                    .ToArray();

            List<VideoItem>[] groups =
                await Task.WhenAll(tasks);

            return groups
                .SelectMany(group => group)
                .ToList();
        }

        private static async Task<List<VideoItem>> GetChannelVideosFromFeedThrottledAsync(
            string channelId,
            int maxAgeDays,
            SemaphoreSlim gate,
            CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);

            try
            {
                return await GetChannelVideosFromFeedAsync(
                    channelId,
                    maxAgeDays,
                    cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        private static async Task<List<VideoItem>> GetChannelVideosFromFeedAsync(
            string channelId,
            int maxAgeDays,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return [];

            string requestUri =
                "https://www.youtube.com/feeds/videos.xml" +
                $"?channel_id={Uri.EscapeDataString(channelId)}";

            try
            {
                string xml =
                    await HttpClient.GetStringAsync(
                        requestUri,
                        cancellationToken);

                XDocument document =
                    XDocument.Parse(xml);

                XNamespace atom = "http://www.w3.org/2005/Atom";
                XNamespace yt = "http://www.youtube.com/xml/schemas/2015";
                XNamespace media = "http://search.yahoo.com/mrss/";

                DateTime minDate =
                    DateTime.Now.Date.AddDays(-Math.Max(1, maxAgeDays));

                return document
                    .Descendants(atom + "entry")
                    .Select(entry =>
                    {
                        DateTimeOffset? publishedAt =
                            DateTimeOffset.TryParse(
                                entry.Element(atom + "published")?.Value,
                                out DateTimeOffset parsedPublishedAt)
                                    ? parsedPublishedAt
                                    : null;

                        string link =
                            entry.Element(atom + "link")?
                                .Attribute("href")?
                                .Value ?? "";

                        return new VideoItem
                        {
                            Id = entry.Element(yt + "videoId")?.Value ?? "",
                            Title = Decode(entry.Element(atom + "title")?.Value),
                            ChannelId = channelId,
                            Channel = Decode(entry.Element(atom + "author")?.Element(atom + "name")?.Value),
                            PublishedAt = FormatPublishedAt(publishedAt),
                            PublishedAtSort = publishedAt,
                            Thumbnail = NormalizeVideoThumbnailUrl(
                                entry
                                    .Descendants(media + "thumbnail")
                                    .FirstOrDefault()?
                                    .Attribute("url")?
                                    .Value ?? ""),
                            Category = "YouTube",
                            IsEmbeddable = true,
                            IsShort = link.Contains(
                                "/shorts/",
                                StringComparison.OrdinalIgnoreCase)
                        };
                    })
                    .Where(video => !string.IsNullOrWhiteSpace(video.Id))
                    .Where(video =>
                        GetPublishedAtSort(video) >= minDate ||
                        GetPublishedAtSort(video) == DateTime.MinValue)
                    .ToList();
            }
            catch (HttpRequestException)
            {
                return [];
            }
            catch (TaskCanceledException)
            {
                return [];
            }
            catch (System.Xml.XmlException)
            {
                return [];
            }
        }

        private static async Task<List<VideoItem>> GetChannelSubscriptionVideosAsync(
            string channelId,
            int maxAgeDays,
            CancellationToken cancellationToken)
        {
            return await GetChannelVideosFromFeedAsync(
                channelId,
                maxAgeDays,
                cancellationToken);
        }

        private static async Task<List<VideoItem>> GetChannelBroadcastsForSubscriptionAsync(
            string channelId,
            CancellationToken cancellationToken)
        {
            List<VideoItem> broadcasts = [];

            broadcasts.AddRange(
                await GetChannelBroadcastsAsync(
                    channelId,
                    "live",
                    cancellationToken));

            broadcasts.AddRange(
                await GetChannelBroadcastsAsync(
                    channelId,
                    "upcoming",
                    cancellationToken));

            if (broadcasts.Count == 0)
            {
                broadcasts.AddRange(
                    await GetChannelLivePageBroadcastsAsync(
                        channelId,
                        cancellationToken));
            }

            return broadcasts;
        }

        private static async Task<List<VideoItem>> GetChannelBroadcastsAsync(
            string channelId,
            string eventType,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return [];

            string requestUri =
                "https://www.googleapis.com/youtube/v3/search" +
                "?part=snippet&type=video&maxResults=5" +
                $"&channelId={Uri.EscapeDataString(channelId)}" +
                $"&eventType={Uri.EscapeDataString(eventType)}";

            try
            {
                using HttpResponseMessage response =
                    await SendYouTubeGetAsync(
                        requestUri,
                        cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return [];

                await using var contentStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                YouTubeSearchResponse? searchResponse =
                    await JsonSerializer.DeserializeAsync<YouTubeSearchResponse>(
                        contentStream,
                        JsonOptions,
                        cancellationToken);

                bool isLive =
                    eventType.Equals("live", StringComparison.OrdinalIgnoreCase);

                bool isPremiere =
                    eventType.Equals("upcoming", StringComparison.OrdinalIgnoreCase);

                return searchResponse?.Items?
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id?.VideoId))
                    .Select(item => new VideoItem
                    {
                        Id = item.Id?.VideoId ?? "",
                        Title = Decode(item.Snippet?.Title),
                        Channel = Decode(item.Snippet?.ChannelTitle),
                        ChannelId = item.Snippet?.ChannelId ?? channelId,
                        PublishedAt = FormatPublishedAt(item.Snippet?.PublishedAt),
                        PublishedAtSort = item.Snippet?.PublishedAt,
                        Thumbnail = NormalizeVideoThumbnailUrl(
                            item.Snippet?.Thumbnails?.High?.Url ??
                            item.Snippet?.Thumbnails?.Medium?.Url ??
                            item.Snippet?.Thumbnails?.Default?.Url ??
                            ""),
                        IsLive = isLive,
                        IsPremiere = isPremiere,
                        IsEmbeddable = true,
                        Category = "YouTube"
                    })
                    .ToList() ?? [];
            }
            catch (HttpRequestException)
            {
                return [];
            }
            catch (JsonException)
            {
                return [];
            }
            catch (TaskCanceledException)
            {
                return [];
            }
        }

        private static async Task<List<VideoItem>> GetChannelLivePageBroadcastsAsync(
            string channelId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return [];

            try
            {
                using HttpResponseMessage response =
                    await HttpClient.GetAsync(
                        $"https://www.youtube.com/channel/{Uri.EscapeDataString(channelId)}/live",
                        cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return [];

                Uri? finalUri =
                    response.RequestMessage?.RequestUri;

                bool navigatedToLiveVideo =
                    finalUri != null &&
                    (finalUri.AbsolutePath.Contains(
                            "/watch",
                            StringComparison.OrdinalIgnoreCase) ||
                        finalUri.AbsolutePath.Contains(
                            "/live/",
                            StringComparison.OrdinalIgnoreCase) ||
                        finalUri.Query.Contains(
                            "v=",
                            StringComparison.OrdinalIgnoreCase));

                if (!navigatedToLiveVideo)
                    return [];

                if (finalUri == null)
                    return [];

                string html =
                    await response.Content.ReadAsStringAsync(cancellationToken);

                string videoId =
                    ExtractVideoId(finalUri.ToString());

                if (string.IsNullOrWhiteSpace(videoId))
                {
                    videoId =
                        MatchValue(html, @"""videoId"":""(?<value>[^""]+)""");
                }

                if (string.IsNullOrWhiteSpace(videoId))
                    return [];

                string title =
                    MatchJsonText(html, "title");

                bool isPremiere =
                    html.Contains(
                        "upcomingEventData",
                        StringComparison.OrdinalIgnoreCase) ||
                    html.Contains(
                        @"""isUpcoming"":true",
                        StringComparison.OrdinalIgnoreCase);

                bool isLive =
                    !isPremiere &&
                    (html.Contains(
                            @"""isLiveNow"":true",
                            StringComparison.OrdinalIgnoreCase) ||
                        html.Contains(
                            @"""isLive"":true",
                            StringComparison.OrdinalIgnoreCase) ||
                        html.Contains(
                            @"""isLiveContent"":true",
                            StringComparison.OrdinalIgnoreCase) ||
                        html.Contains(
                            @"""isLivePlayback"":true",
                            StringComparison.OrdinalIgnoreCase) ||
                        html.Contains(
                            "liveBroadcastDetails",
                            StringComparison.OrdinalIgnoreCase));

                if (!isLive && !isPremiere)
                    return [];

                return
                [
                    new VideoItem
                    {
                        Id = videoId,
                        Title = title,
                        ChannelId = channelId,
                        PublishedAtSort = DateTimeOffset.Now,
                        PublishedAt = FormatPublishedAt(DateTimeOffset.Now),
                        Thumbnail = NormalizeVideoThumbnailUrl(
                            MatchBestYouTubeImageUrl(html, "i.ytimg.com")),
                        IsLive = isLive,
                        IsPremiere = isPremiere,
                        IsEmbeddable = true,
                        Category = "YouTube"
                    }
                ];
            }
            catch (HttpRequestException)
            {
                return [];
            }
            catch (TaskCanceledException)
            {
                return [];
            }
            catch (RegexMatchTimeoutException)
            {
                return [];
            }
        }

        private static async Task<string> GetUploadsPlaylistIdAsync(
            string channelId,
            CancellationToken cancellationToken)
        {
            string requestUri =
                "https://www.googleapis.com/youtube/v3/channels" +
                "?part=contentDetails" +
                $"&id={Uri.EscapeDataString(channelId)}";

            using HttpResponseMessage response =
                await SendYouTubeGetAsync(
                    requestUri,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
                return "";

            await using var contentStream =
                await response.Content.ReadAsStreamAsync(cancellationToken);

            YouTubeChannelsResponse? channelsResponse =
                await JsonSerializer.DeserializeAsync<YouTubeChannelsResponse>(
                    contentStream,
                    JsonOptions,
                    cancellationToken);

            return channelsResponse?.Items?
                .FirstOrDefault()?
                .ContentDetails?
                .RelatedPlaylists?
                .Uploads ?? "";
        }

        private static async Task<HttpResponseMessage> SendYouTubeGetAsync(
            string requestUri,
            CancellationToken cancellationToken)
        {
            if (HasApiKey)
            {
                string separator =
                    requestUri.Contains('?') ? "&" : "?";

                return await HttpClient.GetAsync(
                    requestUri +
                    separator +
                    $"key={Uri.EscapeDataString(AppSettings.YouTubeApiKey)}",
                    cancellationToken);
            }

            string accessToken =
                await ServiceLocator.GoogleOAuth.GetAccessTokenAsync();

            if (string.IsNullOrWhiteSpace(accessToken))
                return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);

            return await SendAuthorizedGetAsync(
                requestUri,
                accessToken,
                cancellationToken);
        }

        private static async Task<HttpResponseMessage> SendAuthorizedGetAsync(
            string requestUri,
            string accessToken,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                requestUri);

            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            return await HttpClient.SendAsync(request, cancellationToken);
        }

        private static string Decode(string? value)
        {
            return WebUtility.HtmlDecode(value ?? "");
        }

        private static async Task<List<VideoItem>> GetInvidiousHomeVideosAsync(
            CancellationToken cancellationToken,
            bool includeAdditionalFeeds = false)
        {
            string[] instances =
            [
                "https://yewtu.be",
                "https://inv.nadeko.net",
                "https://invidious.nerdvpn.de"
            ];

            string[] paths =
                includeAdditionalFeeds
                    ? [
                        "/api/v1/popular",
                        "/api/v1/trending?type=default&region=US",
                        "/api/v1/trending?type=music&region=US",
                        "/api/v1/trending?type=gaming&region=US",
                        "/api/v1/trending?type=movies&region=US"
                    ]
                    : [
                        "/api/v1/popular",
                        "/api/v1/trending?type=default&region=US"
                    ];

            foreach (string instance in instances)
            {
                List<VideoItem> videos = [];

                foreach (string path in paths)
                {
                    videos.AddRange(
                        await TryGetInvidiousVideosAsync(
                            instance,
                            path,
                            cancellationToken));
                }

                videos =
                    videos
                        .Where(video => !string.IsNullOrWhiteSpace(video.Id))
                        .GroupBy(video => video.Id)
                        .Select(group => group.First())
                        .Take(includeAdditionalFeeds ? 80 : 24)
                        .ToList();

                if (videos.Count > 0)
                    return videos;
            }

            return [];
        }

        private static async Task<List<VideoItem>> SearchPipedAsync(
            string query,
            CancellationToken cancellationToken)
        {
            string[] instances =
            [
                "https://pipedapi.kavin.rocks",
                "https://pipedapi.adminforge.de",
                "https://pipedapi.syncpundit.io"
            ];

            foreach (string instance in instances)
            {
                try
                {
                    using HttpResponseMessage response =
                        await HttpClient.GetAsync(
                            $"{instance}/search?q={Uri.EscapeDataString(query)}&filter=videos",
                            cancellationToken);

                    response.EnsureSuccessStatusCode();

                    await using var stream =
                        await response.Content.ReadAsStreamAsync(cancellationToken);

                    JsonDocument document =
                        await JsonDocument.ParseAsync(
                            stream,
                            cancellationToken: cancellationToken);

                    if (!document.RootElement.TryGetProperty("items", out JsonElement items) ||
                        items.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    List<VideoItem> videos = [];

                    foreach (JsonElement item in items.EnumerateArray())
                    {
                        string url = GetJsonString(item, "url");
                        string videoId = ExtractVideoId(url);

                        if (string.IsNullOrWhiteSpace(videoId))
                            continue;

                        videos.Add(
                            new VideoItem
                            {
                                Id = videoId,
                                Title = Decode(GetJsonString(item, "title")),
                                Channel = Decode(GetJsonString(item, "uploaderName")),
                                Views = FormatViewCount(GetJsonNumber(item, "views")?.ToString(CultureInfo.InvariantCulture)),
                                Duration = FormatDuration(ToInt32(GetJsonNumber(item, "duration"))),
                                PublishedAt = Decode(GetJsonString(item, "uploadedDate")),
                                Thumbnail = GetJsonString(item, "thumbnail"),
                                Category = "YouTube",
                                IsEmbeddable = true
                            });
                    }

                    if (videos.Count > 0)
                        return videos.Take(24).ToList();
                }
                catch (HttpRequestException)
                {
                }
                catch (JsonException)
                {
                }
                catch (TaskCanceledException)
                {
                }
            }

            return [];
        }

        private static string GetJsonString(
            JsonElement element,
            string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? ""
                    : "";
        }

        private static long? GetJsonNumber(
            JsonElement element,
            string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
                return null;

            return value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt64(out long result)
                    ? result
                    : null;
        }

        private static int? ToInt32(long? value)
        {
            if (value == null ||
                value.Value > int.MaxValue)
            {
                return null;
            }

            return (int)value.Value;
        }

        private static string ExtractVideoId(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            int markerIndex =
                url.IndexOf("v=", StringComparison.Ordinal);

            if (markerIndex < 0)
                return "";

            string id =
                url[(markerIndex + 2)..];

            int parameterIndex =
                id.IndexOf('&', StringComparison.Ordinal);

            return parameterIndex >= 0
                ? id[..parameterIndex]
                : id;
        }

        private static async Task<List<VideoItem>> SearchInvidiousAsync(
            string query,
            CancellationToken cancellationToken)
        {
            string[] instances =
            [
                "https://yewtu.be",
                "https://inv.nadeko.net",
                "https://invidious.nerdvpn.de"
            ];

            foreach (string instance in instances)
            {
                List<VideoItem> videos =
                    await TryGetInvidiousVideosAsync(
                        instance,
                        $"/api/v1/search?type=video&q={Uri.EscapeDataString(query)}",
                        cancellationToken);

                if (videos.Count > 0)
                    return videos.Take(24).ToList();
            }

            return await SearchPipedAsync(query, cancellationToken);
        }

        private static async Task<List<SearchResultItem>> SearchYouTubeResultsPageAsync(
            string query,
            CancellationToken cancellationToken)
        {
            try
            {
                string html =
                    await HttpClient.GetStringAsync(
                        $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}",
                        cancellationToken);

                List<SearchResultItem> results = [];

                foreach (Match match in Regex.Matches(
                    html,
                    @"""channelRenderer"":\{(?<body>.*?""navigationEndpoint"":\{.*?\})",
                    RegexOptions.Singleline))
                {
                    string body = match.Groups["body"].Value;
                    string channelId = MatchValue(body, @"""channelId"":""(?<value>[^""]+)""");
                    if (string.IsNullOrWhiteSpace(channelId))
                    {
                        channelId = MatchValue(body, @"""browseId"":""(?<value>[^""]+)""");
                    }

                    string title = MatchJsonText(body, "title");

                    if (string.IsNullOrWhiteSpace(channelId) ||
                        string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }

                    results.Add(
                        new SearchResultItem
                        {
                            Kind = "Channel",
                            Channel = new ChannelItem
                            {
                                Id = channelId,
                                Title = title,
                                Description = MatchJsonText(body, "descriptionSnippet"),
                                Thumbnail = MatchBestYouTubeImageUrl(body, "yt3")
                            }
                        });

                    if (results.Count >= 8)
                        break;
                }

                foreach (Match match in Regex.Matches(
                    html,
                    @"""videoRenderer"":\{(?<body>.*?""videoId"":""(?<id>[^""]+)""(?<rest>.*?))""thumbnailOverlays""",
                    RegexOptions.Singleline))
                {
                    string body =
                        match.Groups["body"].Value +
                        match.Groups["rest"].Value;
                    string videoId = match.Groups["id"].Value;
                    string title = MatchJsonText(body, "title");
                    string channelId = MatchValue(body, @"""browseId"":""(?<value>UC[^""]+)""");
                    bool isShort =
                        body.Contains(@"/shorts/", StringComparison.OrdinalIgnoreCase) ||
                        body.Contains(@"\/shorts\/", StringComparison.OrdinalIgnoreCase);

                    if (string.IsNullOrWhiteSpace(videoId) ||
                        string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }

                    results.Add(
                        new SearchResultItem
                        {
                            Kind = "Video",
                            Video = new VideoItem
                            {
                                Id = videoId,
                                Title = title,
                                Channel = MatchJsonText(body, "ownerText"),
                                ChannelId = channelId,
                                Views = MatchJsonText(body, "viewCountText"),
                                Duration = MatchJsonText(body, "lengthText"),
                                PublishedAt = MatchJsonText(body, "publishedTimeText"),
                                Thumbnail = NormalizeVideoThumbnailUrl(
                                    MatchBestYouTubeImageUrl(body, "i.ytimg.com")),
                                IsShort = isShort,
                                Category = "YouTube",
                                IsEmbeddable = true
                            }
                        });

                    if (results.Count >= 32)
                        break;
                }

                await TryEnrichSearchChannelsAsync(
                    results,
                    cancellationToken);

                return results;
            }
            catch (HttpRequestException)
            {
                return [];
            }
            catch (TaskCanceledException)
            {
                return [];
            }
            catch (RegexMatchTimeoutException)
            {
                return [];
            }
        }

        private static string MatchJsonText(
            string body,
            string propertyName)
        {
            string escapedProperty =
                Regex.Escape(propertyName);

            string value =
                MatchValue(
                    body,
                    $@"""{escapedProperty}"":\{{""simpleText"":""(?<value>[^""]+)""");

            if (!string.IsNullOrWhiteSpace(value))
                return DecodeJsonString(value);

            value =
                MatchValue(
                    body,
                    $@"""{escapedProperty}"":\{{""runs"":\[\{{""text"":""(?<value>[^""]+)""");

            return DecodeJsonString(value);
        }

        private static async Task TryEnrichSearchChannelsAsync(
            List<SearchResultItem> results,
            CancellationToken cancellationToken)
        {
            List<ChannelItem> channels =
                results
                    .Where(result => result.Kind == "Channel")
                    .Select(result => result.Channel)
                    .Where(channel =>
                        channel != null &&
                        !string.IsNullOrWhiteSpace(channel.Id) &&
                        string.IsNullOrWhiteSpace(channel.Thumbnail))
                    .Select(channel => channel!)
                    .ToList();

            if (channels.Count == 0)
                return;

            string channelIds =
                string.Join(
                    ",",
                    channels
                        .Select(channel => channel.Id)
                        .Distinct()
                        .Take(50));

            if (string.IsNullOrWhiteSpace(channelIds))
                return;

            string requestUri =
                "https://www.googleapis.com/youtube/v3/channels" +
                "?part=snippet" +
                $"&id={Uri.EscapeDataString(channelIds)}";

            try
            {
                using HttpResponseMessage response =
                    await SendYouTubeGetAsync(
                        requestUri,
                        cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return;

                await using var contentStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                YouTubeChannelsResponse? channelsResponse =
                    await JsonSerializer.DeserializeAsync<YouTubeChannelsResponse>(
                        contentStream,
                        JsonOptions,
                        cancellationToken);

                Dictionary<string, YouTubeChannel> channelsById =
                    channelsResponse?.Items?
                        .Where(channel => !string.IsNullOrWhiteSpace(channel.Id))
                        .ToDictionary(channel => channel.Id ?? "", channel => channel) ?? [];

                foreach (ChannelItem channel in channels)
                {
                    if (!channelsById.TryGetValue(channel.Id, out YouTubeChannel? enrichedChannel))
                        continue;

                    channel.Thumbnail =
                        NormalizeYouTubeImageUrl(
                            enrichedChannel.Snippet?.Thumbnails?.High?.Url ??
                            enrichedChannel.Snippet?.Thumbnails?.Medium?.Url ??
                            enrichedChannel.Snippet?.Thumbnails?.Default?.Url ??
                            channel.Thumbnail);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException ||
                ex is JsonException ||
                ex is TaskCanceledException)
            {
            }
        }

        private static string MatchValue(
            string body,
            string pattern)
        {
            Match match =
                Regex.Match(
                    body,
                    pattern,
                    RegexOptions.Singleline,
                    TimeSpan.FromMilliseconds(250));

            return match.Success
                ? match.Groups["value"].Value
                : "";
        }

        private static string MatchBestYouTubeImageUrl(
            string body,
            string hostNeedle)
        {
            List<string> matches =
                Regex.Matches(
                    body,
                    @"""url"":""(?<value>https:[^""]+)""",
                    RegexOptions.Singleline,
                    TimeSpan.FromMilliseconds(250))
                    .Select(match => NormalizeYouTubeImageUrl(match.Groups["value"].Value))
                    .Where(value =>
                        value.Contains(hostNeedle, StringComparison.OrdinalIgnoreCase) &&
                        Uri.TryCreate(value, UriKind.Absolute, out _))
                    .Distinct()
                    .ToList();

            return matches
                .OrderByDescending(value => value.Contains("=s176", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(value => value.Contains("=s88", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(value => value.Length)
                .FirstOrDefault() ?? "";
        }

        private static string DecodeJsonString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            try
            {
                return Decode(
                    JsonSerializer.Deserialize<string>(
                        $"\"{value}\"") ?? value);
            }
            catch (JsonException)
            {
                return Decode(value);
            }
        }

        private static string NormalizeYouTubeImageUrl(string value)
        {
            return DecodeJsonString(value)
                .Replace(@"\u0026", "&", StringComparison.Ordinal)
                .Replace(@"\u003d", "=", StringComparison.Ordinal)
                .Replace(@"\/", "/", StringComparison.Ordinal)
                .Replace(@"\u002F", "/", StringComparison.Ordinal);
        }

        private static string NormalizeVideoThumbnailUrl(string value)
        {
            string thumbnail =
                NormalizeYouTubeImageUrl(value);

            if (thumbnail.StartsWith("//", StringComparison.Ordinal))
                thumbnail = "https:" + thumbnail;

            if (string.IsNullOrWhiteSpace(thumbnail) ||
                !thumbnail.Contains("ytimg.com/vi/", StringComparison.OrdinalIgnoreCase))
            {
                return thumbnail;
            }

            return Regex.Replace(
                thumbnail,
                @"/(?:default|mqdefault|hqdefault|sddefault)\.jpg",
                "/hq720.jpg",
                RegexOptions.IgnoreCase);
        }

        private static async Task<List<VideoItem>> TryGetInvidiousVideosAsync(
            string instance,
            string path,
            CancellationToken cancellationToken)
        {
            try
            {
                using HttpResponseMessage response =
                    await HttpClient.GetAsync(instance + path, cancellationToken);

                response.EnsureSuccessStatusCode();

                await using var contentStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);

                JsonDocument document =
                    await JsonDocument.ParseAsync(
                        contentStream,
                        cancellationToken: cancellationToken);

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return [];

                List<VideoItem> videos = [];

                foreach (JsonElement item in document.RootElement.EnumerateArray())
                {
                    string videoId = GetJsonString(item, "videoId");

                    if (string.IsNullOrWhiteSpace(videoId))
                        continue;

                    videos.Add(
                        new VideoItem
                        {
                            Id = videoId,
                            Title = Decode(GetJsonString(item, "title")),
                            Channel = Decode(GetJsonString(item, "author")),
                            Views = FormatViewCount(GetJsonNumber(item, "viewCount")?.ToString(CultureInfo.InvariantCulture)),
                            Duration = FormatDuration(ToInt32(GetJsonNumber(item, "lengthSeconds"))),
                            PublishedAt = Decode(GetJsonString(item, "publishedText")),
                            Thumbnail = GetInvidiousThumbnail(instance, item),
                            Category = "YouTube",
                            IsEmbeddable = true
                        });
                }

                return videos;
            }
            catch (HttpRequestException)
            {
                return [];
            }
            catch (JsonException)
            {
                return [];
            }
            catch (TaskCanceledException)
            {
                return [];
            }
        }

        private static string GetInvidiousThumbnail(
            string instance,
            List<InvidiousThumbnail>? thumbnails)
        {
            string thumbnail =
                thumbnails?
                    .OrderByDescending(item => item.Width)
                    .FirstOrDefault()?
                    .Url ?? "";

            if (thumbnail.StartsWith("//", StringComparison.Ordinal))
                return "https:" + thumbnail;

            if (thumbnail.StartsWith("/", StringComparison.Ordinal))
                return instance + thumbnail;

            return thumbnail;
        }

        private static string GetInvidiousThumbnail(
            string instance,
            JsonElement item)
        {
            if (!item.TryGetProperty("videoThumbnails", out JsonElement thumbnails) ||
                thumbnails.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            string thumbnail = "";
            int bestWidth = -1;

            foreach (JsonElement candidate in thumbnails.EnumerateArray())
            {
                string url = GetJsonString(candidate, "url");
                int width = ToInt32(GetJsonNumber(candidate, "width")) ?? 0;

                if (!string.IsNullOrWhiteSpace(url) && width >= bestWidth)
                {
                    thumbnail = url;
                    bestWidth = width;
                }
            }

            if (thumbnail.StartsWith("//", StringComparison.Ordinal))
                return "https:" + thumbnail;

            if (thumbnail.StartsWith("/", StringComparison.Ordinal))
                return instance + thumbnail;

            return thumbnail;
        }

        private static string FormatDuration(int? totalSeconds)
        {
            if (totalSeconds == null || totalSeconds.Value <= 0)
                return "";

            TimeSpan duration =
                TimeSpan.FromSeconds(totalSeconds.Value);

            return duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatBannerUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value.Contains('=', StringComparison.Ordinal)
                ? value
                : value + "=w2120-fcrop64=1,00005a57ffffa5a8-k-c0xffffffff-no-nd-rj";
        }

        private static string FormatDuration(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            try
            {
                TimeSpan duration = XmlConvert.ToTimeSpan(value);

                return duration.TotalHours >= 1
                    ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                    : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return "";
            }
        }

        private static string FormatPublishedAt(DateTimeOffset? value)
        {
            if (value == null)
                return "";

            return value.Value.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
        }

        private static List<VideoItem> FilterSubscribedVideos(
            List<VideoItem> videos,
            int maxAgeDays,
            bool includeShorts)
        {
            DateTime minDate =
                DateTime.Now.Date.AddDays(-Math.Max(1, maxAgeDays));

            return videos
                .Where(video =>
                    video.IsLive ||
                    video.IsPremiere ||
                    GetPublishedAtSort(video) >= minDate)
                .Where(video => includeShorts || !IsShort(video))
                .ToList();
        }

        private static CachedSubscribedVideos? LoadCachedSubscribedVideos()
        {
            string filePath =
                Path.Combine(
                    ApplicationData.Current.LocalFolder.Path,
                    CachedSubscribedVideosFile);

            if (!File.Exists(filePath))
                return null;

            try
            {
                string rawValue = File.ReadAllText(filePath);

                CachedSubscribedVideos? cache =
                    JsonSerializer.Deserialize<CachedSubscribedVideos>(
                        rawValue,
                        JsonOptions);

                return cache?.Version == CachedSubscribedVideosVersion &&
                    cache.Videos.Count > 0
                    ? cache
                    : null;
            }
            catch (Exception ex) when (ex is JsonException ||
                ex is IOException ||
                ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void SaveCachedSubscribedVideos(
            List<VideoItem> videos,
            int maxAgeDays)
        {
            try
            {
                string filePath =
                    Path.Combine(
                        ApplicationData.Current.LocalFolder.Path,
                        CachedSubscribedVideosFile);

                CachedSubscribedVideos cache = new()
                {
                    Version = CachedSubscribedVideosVersion,
                    MaxAgeDays = maxAgeDays,
                    SavedAt = DateTimeOffset.Now,
                    Videos = videos
                        .Take(300)
                        .ToList()
                };

                File.WriteAllText(
                    filePath,
                    JsonSerializer.Serialize(cache));
            }
            catch (Exception ex) when (ex is IOException ||
                ex is UnauthorizedAccessException)
            {
            }
        }

        private static bool IsShort(VideoItem video)
        {
            if (video.IsShort)
                return true;

            bool titleLooksShort =
                video.Title.Contains("#short", StringComparison.OrdinalIgnoreCase) ||
                video.Title.Contains(" shorts", StringComparison.OrdinalIgnoreCase) ||
                video.Title.Contains(" short ", StringComparison.OrdinalIgnoreCase) ||
                video.Title.EndsWith(" short", StringComparison.OrdinalIgnoreCase) ||
                video.Title.Contains("ytshorts", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(video.Duration))
            {
                return titleLooksShort;
            }

            string[] parts =
                video.Duration.Split(':');

            if (parts.Length == 1 &&
                int.TryParse(parts[0], out int secondsOnly))
            {
                return secondsOnly <= 180 || titleLooksShort;
            }

            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int minutes) &&
                int.TryParse(parts[1], out int seconds))
            {
                int totalSeconds =
                    minutes * 60 + seconds;

                return titleLooksShort ||
                    totalSeconds <= 180;
            }

            return titleLooksShort;
        }

        private static bool LooksLikeBroadcast(VideoItem video)
        {
            string title = video.Title ?? "";

            return title.Contains(" live", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("live ", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("[live]", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("livestream", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("stream", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("24/7", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("premiere", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("upcoming", StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime TryParsePublishedAt(string value)
        {
            return DateTime.TryParse(
                value,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AssumeLocal,
                out DateTime result)
                ? result
                : DateTime.MinValue;
        }

        private static DateTime GetPublishedAtSort(VideoItem video)
        {
            return video.PublishedAtSort?.LocalDateTime ??
                TryParsePublishedAt(video.PublishedAt);
        }

        private static string FormatViewCount(string? value)
        {
            if (!ulong.TryParse(value, out ulong count))
                return "";

            return count switch
            {
                >= 1_000_000_000 => $"{count / 1_000_000_000D:0.#}B views",
                >= 1_000_000 => $"{count / 1_000_000D:0.#}M views",
                >= 1_000 => $"{count / 1_000D:0.#}K views",
                _ => $"{count:N0} views"
            };
        }

        private static string FormatLikeCount(uint? value)
        {
            if (value == null)
                return "";

            return value.Value switch
            {
                >= 1_000_000 => $"{value.Value / 1_000_000D:0.#}M likes",
                >= 1_000 => $"{value.Value / 1_000D:0.#}K likes",
                1 => "1 like",
                _ => $"{value.Value:N0} likes"
            };
        }

        private sealed class YouTubeSearchResponse
        {
            public List<YouTubeSearchItem>? Items { get; set; }
        }

        private sealed class YouTubeSearchItem
        {
            public YouTubeSearchItemId? Id { get; set; }

            public YouTubeSnippet? Snippet { get; set; }
        }

        private sealed class YouTubeSearchItemId
        {
            public string? VideoId { get; set; }

            public string? ChannelId { get; set; }
        }

        private sealed class YouTubeSnippet
        {
            public string? Title { get; set; }

            public string? Description { get; set; }

            public string? ChannelTitle { get; set; }

            public string? ChannelId { get; set; }

            public DateTimeOffset? PublishedAt { get; set; }

            public string? LiveBroadcastContent { get; set; }

            public YouTubeThumbnails? Thumbnails { get; set; }
        }

        private sealed class YouTubeThumbnails
        {
            public YouTubeThumbnail? Default { get; set; }

            public YouTubeThumbnail? Medium { get; set; }

            public YouTubeThumbnail? High { get; set; }
        }

        private sealed class YouTubeThumbnail
        {
            public string? Url { get; set; }
        }

        private sealed class YouTubeVideosResponse
        {
            public List<YouTubeVideoDetails>? Items { get; set; }
        }

        private sealed class YouTubeVideoDetails
        {
            public string? Id { get; set; }

            public YouTubeSnippet? Snippet { get; set; }

            public YouTubeContentDetails? ContentDetails { get; set; }

            public YouTubeStatistics? Statistics { get; set; }

            public YouTubeVideoStatus? Status { get; set; }

            public YouTubeLiveStreamingDetails? LiveStreamingDetails { get; set; }
        }

        private sealed class YouTubeContentDetails
        {
            public string? Duration { get; set; }
        }

        private sealed class YouTubeStatistics
        {
            public string? ViewCount { get; set; }

            public uint? LikeCount { get; set; }
        }

        private sealed class YouTubeVideoStatus
        {
            public bool? Embeddable { get; set; }
        }

        private sealed class YouTubeLiveStreamingDetails
        {
            public string? ActiveLiveChatId { get; set; }
        }

        private sealed class YouTubeCommentThreadsResponse
        {
            public List<YouTubeCommentThread>? Items { get; set; }
        }

        private sealed class YouTubeCommentThread
        {
            public YouTubeCommentThreadSnippet? Snippet { get; set; }
        }

        private sealed class YouTubeCommentThreadSnippet
        {
            public YouTubeComment? TopLevelComment { get; set; }
        }

        private sealed class YouTubeComment
        {
            public YouTubeCommentSnippet? Snippet { get; set; }
        }

        private sealed class YouTubeCommentSnippet
        {
            public string? AuthorDisplayName { get; set; }

            public string? TextDisplay { get; set; }

            public DateTimeOffset? PublishedAt { get; set; }

            public uint? LikeCount { get; set; }
        }

        private sealed class YouTubeLiveChatResponse
        {
            public List<YouTubeLiveChatMessage>? Items { get; set; }
        }

        private sealed class YouTubeLiveChatMessage
        {
            public YouTubeLiveChatSnippet? Snippet { get; set; }

            public YouTubeLiveChatAuthorDetails? AuthorDetails { get; set; }
        }

        private sealed class YouTubeLiveChatSnippet
        {
            public string? DisplayMessage { get; set; }

            public DateTimeOffset? PublishedAt { get; set; }
        }

        private sealed class YouTubeLiveChatAuthorDetails
        {
            public string? DisplayName { get; set; }

            public string? ProfileImageUrl { get; set; }
        }

        private sealed class YouTubeSubscriptionsResponse
        {
            public List<YouTubeSubscriptionItem>? Items { get; set; }

            public string? NextPageToken { get; set; }
        }

        private sealed class YouTubeSubscriptionItem
        {
            public YouTubeSubscriptionSnippet? Snippet { get; set; }
        }

        private sealed class YouTubeSubscriptionSnippet
        {
            public string? Title { get; set; }

            public string? Description { get; set; }

            public DateTimeOffset? PublishedAt { get; set; }

            public YouTubeThumbnails? Thumbnails { get; set; }

            public YouTubeResourceId? ResourceId { get; set; }
        }

        private sealed class YouTubeResourceId
        {
            public string? ChannelId { get; set; }

            public string? VideoId { get; set; }
        }

        private sealed class YouTubePlaylistItemsResponse
        {
            public List<YouTubePlaylistItem>? Items { get; set; }

            public string? NextPageToken { get; set; }
        }

        private sealed class YouTubePlaylistItem
        {
            public YouTubePlaylistItemSnippet? Snippet { get; set; }
        }

        private sealed class YouTubePlaylistItemSnippet
        {
            public string? Title { get; set; }

            public string? Description { get; set; }

            public string? ChannelTitle { get; set; }

            public string? ChannelId { get; set; }

            public string? VideoOwnerChannelId { get; set; }

            public DateTimeOffset? PublishedAt { get; set; }

            public YouTubeThumbnails? Thumbnails { get; set; }

            public YouTubeResourceId? ResourceId { get; set; }
        }

        private sealed class YouTubeChannelsResponse
        {
            public List<YouTubeChannel>? Items { get; set; }
        }

        private sealed class YouTubeChannel
        {
            public string? Id { get; set; }

            public YouTubeChannelSnippet? Snippet { get; set; }

            public YouTubeChannelContentDetails? ContentDetails { get; set; }

            public YouTubeBrandingSettings? BrandingSettings { get; set; }
        }

        private sealed class YouTubeChannelSnippet
        {
            public string? Title { get; set; }

            public string? Description { get; set; }

            public YouTubeThumbnails? Thumbnails { get; set; }
        }

        private sealed class YouTubeChannelContentDetails
        {
            public YouTubeRelatedPlaylists? RelatedPlaylists { get; set; }
        }

        private sealed class YouTubeRelatedPlaylists
        {
            public string? Uploads { get; set; }
        }

        private sealed class YouTubeBrandingSettings
        {
            public YouTubeBrandingImage? Image { get; set; }
        }

        private sealed class YouTubeBrandingImage
        {
            public string? BannerExternalUrl { get; set; }
        }

        private sealed class InvidiousVideo
        {
            public string? Title { get; set; }

            public string? VideoId { get; set; }

            public List<InvidiousThumbnail>? VideoThumbnails { get; set; }

            public int? LengthSeconds { get; set; }

            public long? ViewCount { get; set; }

            public string? Author { get; set; }

            public string? PublishedText { get; set; }
        }

        private sealed class InvidiousThumbnail
        {
            public string? Url { get; set; }

            public int Width { get; set; }
        }

        private sealed class CachedSubscribedVideos
        {
            public int Version { get; set; }

            public int MaxAgeDays { get; set; }

            public DateTimeOffset SavedAt { get; set; }

            public List<VideoItem> Videos { get; set; } = [];
        }
    }
}
