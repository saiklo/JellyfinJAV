namespace JellyfinJav.Providers.R18Provider
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Data.Enums;
    using JellyfinJav.Api;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Providers;
    using MediaBrowser.Model.Entities;
    using MediaBrowser.Model.Providers;
    using Microsoft.Extensions.Logging;

    /// <summary>The provider for R18 videos.</summary>
    public class R18Provider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private readonly ILogger<R18Provider> logger;

#pragma warning disable SA1614
        /// <summary>
        /// Initializes a new instance of the <see cref="R18Provider"/> class.
        /// </summary>
        /// <param name="logger"></param>
        public R18Provider(ILogger<R18Provider> logger)
#pragma warning restore SA1614
        {
            this.logger = logger;
        }

        /// <inheritdoc />
        public string Name => "R18";

        /// <inheritdoc />
        public int Order => 99;

        /// <inheritdoc />
        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancelToken)
        {
            this.logger.LogInformation("[JellyfinJav] R18 - processing: " + info.Name);

            var fileName = string.IsNullOrEmpty(info.Path)
                ? info.Name
                : Path.GetFileNameWithoutExtension(info.Path);

            Api.Video? video = null;

            if (info.ProviderIds.TryGetValue("R18", out var storedId))
            {
                this.logger.LogInformation("[JellyfinJav] R18 - Loading by stored ID: " + storedId);
                var (v, error) = await R18Client.LoadVideoWithError(storedId).ConfigureAwait(false);
                if (v.HasValue)
                {
                    video = v;
                }
                else
                {
                    this.logger.LogWarning("[JellyfinJav] R18 - LoadVideo failed ({Error}), falling back to filename search", error);
                }
            }

            if (!video.HasValue)
            {
                var code = Providers.Utility.ExtractCodeFromFilename(fileName);
                if (code is null)
                {
                    this.logger.LogInformation("[JellyfinJav] R18 - No JAV code found in: " + fileName);
                    return new MetadataResult<Movie>();
                }

                this.logger.LogInformation("[JellyfinJav] R18 - Searching r18.dev for: " + code);
                video = await R18Client.SearchFirst(code).ConfigureAwait(false);
            }

            if (!video.HasValue)
            {
                this.logger.LogInformation("[JellyfinJav] R18 - No result found for: " + fileName);
                return new MetadataResult<Movie>();
            }

            this.logger.LogInformation("[JellyfinJav] R18 - Found metadata: " + video);

            var result = new MetadataResult<Movie>
            {
                Item = new Movie
                {
                    OriginalTitle = video.Value.TitleJa ?? info.Name,
                    Name = Providers.Utility.CreateVideoDisplayName(video.Value),
                    Overview = video.Value.Description,
                    PremiereDate = video.Value.ReleaseDate,
                    ProductionYear = video.Value.ReleaseDate?.Year,
                    RunTimeTicks = video.Value.RuntimeMinutes.HasValue
                        ? TimeSpan.FromMinutes(video.Value.RuntimeMinutes.Value).Ticks
                        : (long?)null,
                    ProviderIds = new Dictionary<string, string> { { "R18", video.Value.Id } },
                    Studios = video.Value.Studio != null ? new[] { video.Value.Studio } : Array.Empty<string>(),
                    Genres = video.Value.Genres.ToArray(),
                    Tags = video.Value.Genres.ToArray(),
                    Tagline = video.Value.Series,
                },
                HasMetadata = true,
            };

            foreach (var person in BuildPeople(video.Value))
            {
                result.AddPerson(person);
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo info, CancellationToken cancelToken)
        {
            var fileName = string.IsNullOrEmpty(info.Path)
                ? info.Name
                : Path.GetFileNameWithoutExtension(info.Path);

            var javCode = Providers.Utility.ExtractCodeFromFilename(fileName);
            if (string.IsNullOrEmpty(javCode))
            {
                return Array.Empty<RemoteSearchResult>();
            }

            this.logger.LogInformation("[JellyfinJav] R18 - Search for code: " + javCode);

            var searchResults = await R18Client.Search(javCode).ConfigureAwait(false);

            if (searchResults == null || !searchResults.Any())
            {
                this.logger.LogInformation("[JellyfinJav] R18 - No results found for: " + javCode);
                return Array.Empty<RemoteSearchResult>();
            }

            return searchResults.Select(video => new RemoteSearchResult
            {
                Name = video.Code,
                ProviderIds = new Dictionary<string, string> { { "R18", video.Id } },
                ImageUrl = video.Cover?.ToString(),
            }).ToList();
        }

        /// <inheritdoc />
        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancelToken)
        {
            return await HttpClient.GetAsync(url, cancelToken).ConfigureAwait(false);
        }

        private static string NormalizeActressName(string name)
        {
            if (Plugin.Instance?.Configuration.ActressNameOrder == ActressNameOrder.LastFirst)
            {
                return string.Join(" ", name.Split(' ').Reverse());
            }

            return name;
        }

        private List<PersonInfo> BuildPeople(Api.Video video)
        {
            var people = new List<PersonInfo>();

            if (!string.IsNullOrWhiteSpace(video.Director))
            {
                PeopleHelper.AddPerson(people, new PersonInfo
                {
                    Name = video.Director,
                    Type = PersonKind.Director,
                });
            }

            foreach (var actress in video.Actresses ?? Enumerable.Empty<string>())
            {
                PeopleHelper.AddPerson(people, new PersonInfo
                {
                    Name = NormalizeActressName(actress),
                    Type = PersonKind.Actor,
                });
            }

            return people;
        }
    }
}
