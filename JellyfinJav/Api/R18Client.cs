namespace JellyfinJav.Api
{
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    /// <summary>A client for the r18.dev JSON API.</summary>
    public static class R18Client
    {
        private static readonly HttpClient HttpClient = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:139.0) Gecko/20100101 Firefox/139.0");
            return client;
        }

        /// <summary>Searches for a video by JAV code and returns a list of results.</summary>
        public static async Task<IEnumerable<VideoResult>> Search(string searchCode)
        {
            var response = await HttpClient
                .GetAsync($"https://r18.dev/videos/vod/movies/detail/-/dvd_id={searchCode}/json")
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Enumerable.Empty<VideoResult>();
            }

            var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var contentId = json["content_id"]?.ToString();
            if (string.IsNullOrEmpty(contentId))
            {
                return Enumerable.Empty<VideoResult>();
            }

            var coverUrl = json["images"]?["jacket_image"]?["large2"]?.ToString();

            return new[]
            {
                new VideoResult
                {
                    Code = searchCode.ToUpperInvariant(),
                    Id = contentId,
                    Cover = coverUrl != null ? new Uri(coverUrl) : null,
                },
            };
        }

        /// <summary>Searches for a video by JAV code and returns the first result.</summary>
        public static async Task<Video?> SearchFirst(string searchCode)
        {
            var results = await Search(searchCode).ConfigureAwait(false);
            var first = results.FirstOrDefault();
            return string.IsNullOrEmpty(first.Id) ? null : await LoadVideo(first.Id).ConfigureAwait(false);
        }

        /// <summary>Loads full video metadata by r18.dev content ID.</summary>
        /// <returns>The parsed video, or null if the API returned an error or unrecognised response.</returns>
        public static async Task<(Video? Video, string? Error)> LoadVideoWithError(string id)
        {
            HttpResponseMessage response;
            try
            {
                response = await HttpClient
                    .GetAsync($"https://r18.dev/videos/vod/movies/detail/-/combined={id}/json")
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return (null, $"HTTP exception: {ex.Message}");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (null, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            JObject json;
            try
            {
                json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return (null, $"JSON parse error: {ex.Message}");
            }

            string? code = json["dvd_id"]?.ToString();
            string? title = json["title_en"]?.ToString();

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(code))
            {
                return (null, $"Missing required fields: dvd_id='{code}' title_en='{title}'");
            }

            string? titleJa = json["title_ja"]?.ToString();

            // Materialise to List so the query is not re-evaluated and the JObject can be released.
            var actresses = (json["actresses"] ?? Enumerable.Empty<JToken>())
                .Select(c => c["name_romaji"]?.ToString() ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            var genres = (json["categories"] ?? Enumerable.Empty<JToken>())
                .Select(c => c["name_en"]?.ToString() ?? string.Empty)
                .Where(g => !string.IsNullOrWhiteSpace(g) && NotSaleGenre(g))
                .ToList();

            string? studio = json["label_name_en"]?.ToString() ?? json["maker_name_en"]?.ToString();
            if (string.IsNullOrWhiteSpace(studio)) studio = null;

            string? cover = json["jacket_full_url"]?.ToString();
            string? coverThumb = json["jacket_thumb_url"]?.ToString();

            string? dateString = json["release_date"]?.ToString();
            DateTime? releaseDate = null;
            if (!string.IsNullOrEmpty(dateString) &&
                DateTime.TryParseExact(dateString, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                releaseDate = parsedDate;
            }

            int? runtimeMinutes = null;
            var runtimeToken = json["runtime_mins"];
            if (runtimeToken != null && runtimeToken.Type != JTokenType.Null)
            {
                runtimeMinutes = runtimeToken.Value<int?>();
            }

            string? director = json["directors"]?.FirstOrDefault()?["name_romaji"]?.ToString();
            if (string.IsNullOrWhiteSpace(director)) director = null;

            string? series = json["series_name_en"]?.ToString();
            if (string.IsNullOrWhiteSpace(series)) series = null;

            string? description = json["comment_en"]?.ToString();
            if (string.IsNullOrWhiteSpace(description)) description = null;

            var galleryImages = (json["gallery"] ?? Enumerable.Empty<JToken>())
                .Select(g => g["image_full"]?.ToString() ?? string.Empty)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .ToList();

            title = NormalizeTitle(title, actresses);

            return (new Video(
                id: id,
                code: code,
                title: title,
                titleJa: titleJa,
                actresses: actresses,
                genres: genres,
                studio: studio,
                cover: cover,
                coverThumb: coverThumb,
                releaseDate: releaseDate,
                runtimeMinutes: runtimeMinutes,
                director: director,
                series: series,
                description: description,
                galleryImages: galleryImages), null);
        }

        /// <summary>Loads full video metadata by r18.dev content ID.</summary>
        public static async Task<Video?> LoadVideo(string id)
        {
            var (video, _) = await LoadVideoWithError(id).ConfigureAwait(false);
            return video;
        }

        private static string NormalizeTitle(string title, IList<string> actresses)
        {
            if (actresses.Count != 1)
            {
                return title;
            }

            string name = actresses[0];
            var rx = new Regex($"^({Regex.Escape(name)} - )?(.+?)( ?-? {Regex.Escape(name)})?$");
            var match = rx.Match(title);

            return match.Success ? match.Groups[2].Value : title;
        }

        private static bool NotSaleGenre(string genre)
        {
            return !Regex.IsMatch(genre, @"\bsale\b", RegexOptions.IgnoreCase);
        }
    }
}
