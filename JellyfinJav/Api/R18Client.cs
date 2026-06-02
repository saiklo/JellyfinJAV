namespace JellyfinJav.Api
{
    using FlareSolverrSharp;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    /// <summary>A client for the r18.dev JSON API.</summary>
    public static class R18Client
    {
        private static readonly HttpClient HttpClient = CreateClient(handler: null);

        private static HttpClient CreateClient(HttpMessageHandler? handler)
        {
            var client = handler != null ? new HttpClient(handler) : new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:139.0) Gecko/20100101 Firefox/139.0");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/javascript, */*; q=0.01");
            client.DefaultRequestHeaders.Add("Referer", "https://r18.dev/");
            return client;
        }

        /// <summary>Fetches a URL, falling back to FlareSolverr on 403 if configured.</summary>
        private static async Task<(string? Body, string? Error)> FetchJson(string url)
        {
            HttpResponseMessage response;
            try
            {
                response = await HttpClient.GetAsync(url).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return (null, $"HTTP exception: {ex.Message}");
            }

            if (response.IsSuccessStatusCode)
            {
                return (await response.Content.ReadAsStringAsync().ConfigureAwait(false), null);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var config = JellyfinJav.Plugin.Instance?.Configuration;
                if (config?.EnableFlareSolverr == true && !string.IsNullOrEmpty(config.FlareSolverrUrl))
                {
                    try
                    {
                        var handler = new ClearanceHandler(config.FlareSolverrUrl);
                        using var fsClient = CreateClient(handler);
                        var fsResponse = await fsClient.GetAsync(url).ConfigureAwait(false);
                        if (fsResponse.IsSuccessStatusCode)
                        {
                            return (await fsResponse.Content.ReadAsStringAsync().ConfigureAwait(false), null);
                        }

                        return (null, $"FlareSolverr also failed: HTTP {(int)fsResponse.StatusCode}");
                    }
                    catch (Exception ex)
                    {
                        return (null, $"FlareSolverr error: {ex.Message}");
                    }
                }
            }

            return (null, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        /// <summary>Searches for a video by JAV code and returns a list of results.</summary>
        public static async Task<IEnumerable<VideoResult>> Search(string searchCode)
        {
            var (body, _) = await FetchJson(
                $"https://r18.dev/videos/vod/movies/detail/-/dvd_id={searchCode}/json")
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(body))
            {
                return Enumerable.Empty<VideoResult>();
            }

            var json = JObject.Parse(body);
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
            if (string.IsNullOrEmpty(first.Id)) return null;
            // Pass the original search code as fallback in case dvd_id is null in the combined response.
            var (video, _) = await LoadVideoWithError(first.Id, fallbackCode: first.Code).ConfigureAwait(false);
            return video;
        }

        /// <summary>Loads full video metadata by r18.dev content ID.</summary>
        /// <param name="id">The r18.dev content ID (e.g. aquco00012).</param>
        /// <param name="fallbackCode">DVD ID to use if the API response has a null dvd_id field.</param>
        /// <returns>The parsed video, or null if the API returned an error or unrecognised response.</returns>
        public static async Task<(Video? Video, string? Error)> LoadVideoWithError(string id, string? fallbackCode = null)
        {
            var (body, fetchError) = await FetchJson(
                $"https://r18.dev/videos/vod/movies/detail/-/combined={id}/json")
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(body))
            {
                return (null, fetchError ?? "Empty response");
            }

            JObject json;
            try
            {
                json = JObject.Parse(body);
            }
            catch (Exception ex)
            {
                return (null, $"JSON parse error: {ex.Message}");
            }

            string? code = json["dvd_id"]?.ToString();
            if (string.IsNullOrEmpty(code))
                code = fallbackCode; // dvd_id is null for some titles in the combined endpoint

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
        public static async Task<Video?> LoadVideo(string id, string? fallbackCode = null)
        {
            var (video, _) = await LoadVideoWithError(id, fallbackCode).ConfigureAwait(false);
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
