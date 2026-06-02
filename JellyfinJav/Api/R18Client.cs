namespace JellyfinJav.Api
{
    using AngleSharp;
    using AngleSharp.Dom;
    using AngleSharp.Html.Dom;
    using AngleSharp.Io;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.Movies;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Reflection.Metadata;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    /// <summary>A web scraping client for r18.com.</summary>
    public static class R18Client
    {
        private static readonly IDictionary<string, string> CensoredWords = new Dictionary<string, string>
        {
            { "S***e", "Slave" },
            { "S*********l", "School Girl" },
            { "S********l", "Schoolgirl" },
            { "Sch**l", "School" },
            { "F***e", "Force" },
            { "F*****g", "Forcing" },
            { "P****h", "Punish" },
            { "M****t", "Molest" },
            { "S*****t", "Student" },
            { "T*****e", "Torture" },
            { "D**g", "Drug" },
            { "H*******e", "Hypnotize" },
            { "C***d", "Child" },
            { "V*****e", "Violate" },
            { "Y********l", "Young Girl" },
            { "A*****t", "Assault" },
            { "D***king", "Drinking" },
            { "D***k", "Drunk" },
            { "V*****t", "Violent" },
            { "S******g", "Sleeping" },
            { "R**e", "Rape" },
            { "R****g", "Raping" },
            { "S**t", "Scat" },
            { "K****r", "Killer" },
            { "H*******m", "Hypnotism" },
            { "G*******g", "Gangbang" },
            { "C*ck", "Cock" },
            { "K*ds", "Kids" },
            { "K****p", "Kidnap" },
            { "A****p", "Asleep" },
            { "U*********s", "Unconscious" },
            { "D******e", "Disgrace" },
            { "P********t", "Passed Out" },
            { "M************n", "Mother And Son" },
        };

        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly IBrowsingContext Context = BrowsingContext.New();

        /// <summary>Searches for a video by jav code.</summary>
        /// <param name="searchCode">The jav code. Ex: ABP-001.</param>
        /// <returns>A list of every matched video.</returns>
        public static async Task<IEnumerable<VideoResult>> Search(string searchCode)
        {
            var videos = new List<VideoResult>();
            var client = new HttpClient();
            var context = new BrowsingContext();

            client.DefaultRequestHeaders.Host = "r18.dev";
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("deflate"));
            client.DefaultRequestHeaders.AcceptLanguage.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("en-US"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:139.0) Gecko/20100101 Firefox/139.0");

            var response = await client.GetAsync($"https://r18.dev/videos/vod/movies/detail/-/dvd_id={searchCode}/json");

            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                var jsonObject = JObject.Parse(jsonContent);
                var contentId = jsonObject["content_id"]?.ToString();
                var large2Url = jsonObject["images"]?["jacket_image"]?["large2"]?.ToString();

                if (!string.IsNullOrEmpty(contentId))
                {
                    videos.Add(new VideoResult
                    {
                        Code = searchCode.ToUpper(),
                        Id = contentId,
                        Cover = large2Url != null ? new Uri(large2Url) : null,
                    });
                }

                return videos;
            }
            else
            {
                return videos;
            }
        }

        /// <summary>Searches for a video by jav code, and returns the first result.</summary>
        /// <param name="searchCode">The jav code. Ex: ABP-001.</param>
        /// <returns>The parsed video.</returns>
        public static async Task<Video?> SearchFirst(string searchCode)
        {
            var results = await Search(searchCode);

            if (results.Any())
            {
                return await LoadVideo(results.FirstOrDefault().Id);
            }
            else
            {
                return null;
            }
        }

        /// <summary>Loads a video by id.</summary>
        /// <param name="id">The r18.dev unique video identifier.</param>
        /// <returns>The parsed video.</returns>
        public static async Task<Video?> LoadVideo(string id)
        {
            var client = new HttpClient();
            var context = new BrowsingContext();

            client.DefaultRequestHeaders.Host = "r18.dev";
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("deflate"));
            client.DefaultRequestHeaders.AcceptLanguage.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("en-US"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:139.0) Gecko/20100101 Firefox/139.0");

            var response = await client.GetAsync($"https://r18.dev/videos/vod/movies/detail/-/combined={id}/json");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            
            var jsonContent = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(jsonContent);

            string? code = json["dvd_id"]?.ToString();
            string? title = json["title_en"]?.ToString();
            var actressesToken = json["actresses"];
            var actresses = actressesToken != null
                ? actressesToken.Select(c => c["name_romaji"]?.ToString() ?? string.Empty).Where(n => !string.IsNullOrWhiteSpace(n))
                : Enumerable.Empty<string>();
            var categoriesToken = json["categories"];
            var genres = categoriesToken != null
                ? categoriesToken.Select(c => c["name_en"]?.ToString() ?? string.Empty).Where(g => !string.IsNullOrWhiteSpace(g) && NotSaleGenre(g))
                : Enumerable.Empty<string>();
            string? studio = json["label_name_en"]?.ToString() ?? json["maker_name_en"]?.ToString();
            string? cover = json["jacket_full_url"]?.ToString();
            string? boxArt = cover?.Replace("pl.jpg", "ps.jpg");

            string? dateString = json["release_date"]?.ToString();
            DateTime? releaseDate = null;
            if (!string.IsNullOrEmpty(dateString) &&
                DateTime.TryParseExact(dateString, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
            {
                releaseDate = parsedDate;
            }

            // Director: field may be a string or an array of objects
            string? director = null;
            var directorToken = json["director"];
            if (directorToken != null)
            {
                if (directorToken.Type == JTokenType.Array)
                    director = directorToken.FirstOrDefault()?["name_en"]?.ToString();
                else
                    director = directorToken.ToString();
            }

            // Series: try several possible field names
            string? series = json["series_name_en"]?.ToString()
                ?? json["series"]?["name_en"]?.ToString()
                ?? json["series_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(series))
                series = null;

            if (title is null || code is null)
            {
                return null;
            }

            title = NormalizeTitle(title, actresses);

            return new Video(
                    id: id,
                    code: code,
                    title: title,
                    actresses: actresses,
                    genres: genres,
                    studio: studio,
                    boxArt: boxArt,
                    cover: cover,
                    releaseDate: releaseDate,
                    director: director,
                    series: series);
        }

        private static string NormalizeActress(string actress)
        {
            var rx = new Regex(@"^(.+?)( ?\(.+\))?$");
            var match = rx.Match(actress);

            if (!match.Success)
            {
                return actress;
            }

            return match.Groups[1].Value;
        }

        private static string NormalizeTitle(string title, IEnumerable<string> actresses)
        {
            if (actresses.Count() != 1)
            {
                return title;
            }

            string? name = actresses.ElementAt(0);
            var rx = new Regex($"^({name} - )?(.+?)( ?-? {name})?$");
            var match = rx.Match(title);

            if (!match.Success)
            {
                return title;
            }

            return match.Groups[2].Value;
        }

        private static bool NotSaleGenre(string? genre)
        {
            var rx = new Regex(@"\bsale\b", RegexOptions.IgnoreCase);
            var match = rx.Match(genre ?? string.Empty);

            return !match.Success;
        }
    }
}