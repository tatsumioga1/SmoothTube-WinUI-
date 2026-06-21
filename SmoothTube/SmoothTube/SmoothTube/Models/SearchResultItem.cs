using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SmoothTube.Models
{
    public class SearchResultItem
    {
        private ImageSource? thumbnailSource;

        public string Kind { get; set; } = "Video";

        public VideoItem? Video { get; set; }

        public ChannelItem? Channel { get; set; }

        public string Title =>
            Kind == "Channel"
                ? Channel?.Title ?? ""
                : Video?.Title ?? "";

        public string Subtitle =>
            Kind == "Channel"
                ? "Channel"
                : Video?.Channel ?? "";

        public Visibility ChannelThumbnailVisibility =>
            Kind == "Channel"
                ? Visibility.Visible
                : Visibility.Collapsed;

        public Visibility VideoThumbnailVisibility =>
            Kind == "Channel"
                ? Visibility.Collapsed
                : Visibility.Visible;

        public string Detail
        {
            get
            {
                if (Kind == "Channel")
                    return Channel?.Description ?? "";

                return string.Join(
                    " - ",
                    new[]
                    {
                        string.IsNullOrWhiteSpace(Video?.Duration)
                            ? ""
                            : $"Duration: {Video.Duration}",
                        string.IsNullOrWhiteSpace(Video?.Views)
                            ? ""
                            : $"Views: {Video.Views}",
                        string.IsNullOrWhiteSpace(Video?.PublishedAt)
                            ? ""
                            : $"Posted: {Video.PublishedAt}"
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
            }
        }

        public string Thumbnail =>
            NormalizeThumbnail(
                Kind == "Channel"
                    ? Channel?.Thumbnail ?? ""
                    : Video?.Thumbnail ?? "");

        public ImageSource? ThumbnailSource
        {
            get
            {
                if (thumbnailSource != null)
                    return thumbnailSource;

                if (!System.Uri.TryCreate(Thumbnail, System.UriKind.Absolute, out System.Uri? uri))
                    return null;

                thumbnailSource =
                    new BitmapImage(uri)
                    {
                        DecodePixelWidth = Kind == "Channel" ? 128 : 240,
                        DecodePixelHeight = Kind == "Channel" ? 128 : 136
                    };

                return thumbnailSource;
            }
        }

        private static string NormalizeThumbnail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (value.StartsWith("//", System.StringComparison.Ordinal))
                value = "https:" + value;

            return System.Uri.TryCreate(value, System.UriKind.Absolute, out System.Uri? uri) &&
                (uri.Scheme == "https" ||
                    uri.Scheme == "http" ||
                    uri.Scheme == "ms-appx" ||
                    uri.Scheme == "file")
                    ? value
                    : "";
        }
    }
}
