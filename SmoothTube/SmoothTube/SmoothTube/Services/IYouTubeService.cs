using SmoothTube.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmoothTube.Services
{
    public interface IYouTubeService
    {
        Task<List<VideoItem>> GetHomeVideosAsync(CancellationToken cancellationToken = default);

        Task<List<VideoItem>> GetMoreHomeVideosAsync(
            IEnumerable<string> existingVideoIds,
            CancellationToken cancellationToken = default);

        Task<List<VideoItem>> GetContinueWatchingAsync(CancellationToken cancellationToken = default);

        Task<List<VideoItem>> GetVideosByCategoryAsync(
            string category,
            CancellationToken cancellationToken = default);

        Task<List<VideoItem>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default);

        Task<VideoItem?> GetVideoAsync(
            string videoId,
            CancellationToken cancellationToken = default);

        Task<bool> RateVideoAsync(
            string videoId,
            string rating,
            CancellationToken cancellationToken = default);

        Task<string> GetVideoRatingAsync(
            string videoId,
            CancellationToken cancellationToken = default);

        Task<bool> SubscribeToChannelAsync(
            string channelId,
            CancellationToken cancellationToken = default);

        Task<bool> IsSubscribedToChannelAsync(
            string channelId,
            CancellationToken cancellationToken = default);

        Task<List<SearchResultItem>> SearchAllAsync(
            string query,
            CancellationToken cancellationToken = default);

        Task<List<CommentItem>> GetCommentsAsync(
            string videoId,
            CancellationToken cancellationToken = default);

        Task<List<LiveChatMessageItem>> GetLiveChatMessagesAsync(
            string liveChatId,
            CancellationToken cancellationToken = default);

        Task<List<ChannelItem>> GetSubscriptionsAsync(
            CancellationToken cancellationToken = default);

        Task<List<VideoItem>> GetSubscribedVideosAsync(
            int maxAgeDays = 30,
            bool includeShorts = true,
            CancellationToken cancellationToken = default);

        IAsyncEnumerable<List<VideoItem>> GetSubscribedVideoBatchesAsync(
            int maxAgeDays = 30,
            bool includeShorts = true,
            CancellationToken cancellationToken = default);

        Task<List<VideoItem>> GetSubscribedBroadcastsAsync(
            CancellationToken cancellationToken = default);

        void ClearSubscribedVideoCache();

        Task<List<VideoItem>> GetChannelVideosAsync(
            string channelId,
            CancellationToken cancellationToken = default);

        Task<List<VideoItem>> GetChannelVideosAsync(
            string channelId,
            int maxResults,
            CancellationToken cancellationToken = default);

        Task<ChannelItem?> GetChannelAsync(
            string channelId,
            CancellationToken cancellationToken = default);
    }
}
