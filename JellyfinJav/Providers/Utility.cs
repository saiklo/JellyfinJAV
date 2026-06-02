namespace JellyfinJav.Providers
{
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Controller.Providers;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    /// <summary>A general utility class for random functions.</summary>
    public static class Utility
    {
        // Matches the base JAV code: letters optionally followed by a dash then 2-5 digits.
        private static readonly Regex JavCodeRegex = new Regex(
            @"([A-Za-z]+)-?(\d{2,5})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches common multi-part suffixes that appear after the code:
        // -1, -2, pt1, pt2, part1, a, b, c, cd1, disc2 ...
        private static readonly Regex PartSuffixRegex = new Regex(
            @"^[-_\s]*(pt|part|cd|disc|disk)?[-_\s]*([0-9]+|[a-d])$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// When setting the video title in a Provider, we lose the JAV code details in MovieInfo.
        /// So this is used to retrieve the JAV code to then be able to search using a different Provider.
        /// </summary>
        /// <param name="info">The video's info.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager" />.</param>
        /// <returns>The video's original title.</returns>
        public static string GetVideoOriginalTitle(MovieInfo info, ILibraryManager libraryManager)
        {
            var searchQuery = new InternalItemsQuery
            {
                Name = info.Name,
            };
            var result = libraryManager.GetItemList(searchQuery).FirstOrDefault();

            if (result == null)
            {
                return info.Name;
            }

            return result.OriginalTitle ?? result.Name;
        }

        /// <summary>Extracts the jav code from a video's filename, stripping any multi-part suffix.</summary>
        /// <param name="filename">The video's filename (without extension).</param>
        /// <returns>The normalized JAV code (e.g. MIDE-001), or null if not found.</returns>
        public static string? ExtractCodeFromFilename(string filename)
        {
            var (code, _, _) = ExtractCodeAndPartFromFilename(filename);
            return code;
        }

        /// <summary>
        /// Extracts the JAV code and detects multi-part suffixes (pt1, pt2, -1, -2, a, b, etc.).
        /// </summary>
        /// <param name="filename">The video's filename (without extension).</param>
        /// <returns>
        /// A tuple of (Code, IsMultiPart, PartNumber).
        /// PartNumber is 1-based: 'a'=1, 'b'=2, or the numeric suffix.
        /// </returns>
        public static (string? Code, bool IsMultiPart, int? PartNumber) ExtractCodeAndPartFromFilename(string filename)
        {
            var match = JavCodeRegex.Match(filename);
            if (!match.Success)
            {
                return (null, false, null);
            }

            string letters = match.Groups[1].Value.ToUpperInvariant();
            string digits = match.Groups[2].Value;

            if (letters == "DSVR")
            {
                letters = "3DSVR";
            }

            string code = $"{letters}-{digits}";
            string remainder = filename.Substring(match.Index + match.Length);

            var partMatch = PartSuffixRegex.Match(remainder);
            if (partMatch.Success)
            {
                string partStr = partMatch.Groups[2].Value;
                int partNumber = int.TryParse(partStr, out int n)
                    ? n
                    : (partStr.ToLowerInvariant()[0] - 'a' + 1);
                return (code, true, partNumber);
            }

            return (code, false, null);
        }

        /// <summary>Creates a video's display name according to the plugin's selected configuration.</summary>
        /// <param name="video">The video.</param>
        /// <returns>The video's created display name.</returns>
        public static string CreateVideoDisplayName(Api.Video video)
        {
            return Plugin.Instance?.Configuration.VideoDisplayName switch
            {
                VideoDisplayName.CodeTitle => video.Code + " " + video.Title,
                VideoDisplayName.Title => video.Title,
                _ => throw new System.Exception("Impossible to reach.")
            };
        }

        /// <summary>Crops a full size dvd cover into just the front cover image.</summary>
        /// <param name="httpResponse">The full size dvd cover's http response.</param>
        /// <returns>An empty task when the job is done.</returns>
        public static async Task CropThumb(HttpResponseMessage httpResponse)
        {
            using (var imageStream = await httpResponse.Content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                using var originalImageBitmap = new Bitmap(imageStream);
                var subsetWidth = 379;
                var subsetHeight = 538;

                // create a new bitmap with the desired dimensions
                using (var subset = new Bitmap(subsetWidth, subsetHeight))
                {
                    // draw original image to new bitmap starting from x=421 and y=0
                    using (Graphics gfx = Graphics.FromImage(subset))
                    {
                        Rectangle srcRect = new Rectangle(421, 0, subsetWidth, subsetHeight);
                        gfx.DrawImage(originalImageBitmap, 0, 0, srcRect, GraphicsUnit.Pixel);
                    }

                    var finalStream = File.Open(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jpg"), FileMode.Create);
                    subset.Save(finalStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                    finalStream.Seek(0, SeekOrigin.Begin);

                    var newContent = new StreamContent(finalStream);
                    newContent.Headers.ContentType = httpResponse.Content.Headers.ContentType;
                    httpResponse.Content = newContent;
                }
            }
        }
    }
}