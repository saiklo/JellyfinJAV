namespace JellyfinJav.Providers.R18Provider
{
    using JellyfinJav.Api;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>The provider for R18 video images.</summary>
    public class R18ImageProvider : IRemoteImageProvider, IHasOrder
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        /// <summary>Initializes a new instance of the <see cref="R18ImageProvider"/> class.</summary>
        public R18ImageProvider()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        }

        /// <inheritdoc />
        public string Name => "R18";

        /// <inheritdoc />
        public int Order => 99;

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancelToken)
        {
            var id = item.GetProviderId("R18");
            if (string.IsNullOrEmpty(id))
            {
                return Array.Empty<RemoteImageInfo>();
            }

            var video = await R18Client.LoadVideo(id).ConfigureAwait(false);
            if (!video.HasValue)
            {
                return Array.Empty<RemoteImageInfo>();
            }

            var images = new List<RemoteImageInfo>();

            // Primary poster: prefer the pre-cropped thumb, fall back to full jacket.
            var primaryUrl = video.Value.CoverThumb ?? video.Value.Cover;
            if (!string.IsNullOrEmpty(primaryUrl))
            {
                images.Add(new RemoteImageInfo
                {
                    ProviderName = this.Name,
                    Type = ImageType.Primary,
                    Url = primaryUrl,
                });
            }

            // Full jacket as an Art/Box image.
            if (!string.IsNullOrEmpty(video.Value.Cover))
            {
                images.Add(new RemoteImageInfo
                {
                    ProviderName = this.Name,
                    Type = ImageType.Art,
                    Url = video.Value.Cover,
                });
            }

            // Gallery images as backdrops.
            foreach (var galleryUrl in video.Value.GalleryImages)
            {
                images.Add(new RemoteImageInfo
                {
                    ProviderName = this.Name,
                    Type = ImageType.Backdrop,
                    Url = galleryUrl,
                });
            }

            return images;
        }

        /// <inheritdoc />
        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancelToken)
        {
            return await HttpClient.GetAsync(url, cancelToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary, ImageType.Art, ImageType.Backdrop };
        }

        /// <inheritdoc />
        public bool Supports(BaseItem item) => item is Movie;
    }
}
