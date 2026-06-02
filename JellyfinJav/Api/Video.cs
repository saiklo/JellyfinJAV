namespace JellyfinJav.Api
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>A struct representing a japanese adult video (JAV).</summary>
    public readonly struct Video
    {
        /// <summary>The website-specific identifier.</summary>
        public readonly string Id;

        /// <summary>The jav code. Ex: ABP-001.</summary>
        public readonly string Code;

        /// <summary>The video's English title.</summary>
        public readonly string Title;

        /// <summary>The video's original Japanese title.</summary>
        public readonly string? TitleJa;

        /// <summary>A list of every actress in the video.</summary>
        public readonly IEnumerable<string> Actresses;

        /// <summary>A list of the video's genres.</summary>
        public readonly IEnumerable<string> Genres;

        /// <summary>The studio which released the video.</summary>
        public readonly string? Studio;

        /// <summary>An absolute url to the full jacket image (front + back cover).</summary>
        public readonly string? Cover;

        /// <summary>An absolute url to the cropped front cover thumbnail.</summary>
        public readonly string? CoverThumb;

        /// <summary>The date which the video was released.</summary>
        public readonly DateTime? ReleaseDate;

        /// <summary>Runtime in minutes.</summary>
        public readonly int? RuntimeMinutes;

        /// <summary>The director of the video.</summary>
        public readonly string? Director;

        /// <summary>The series this video belongs to.</summary>
        public readonly string? Series;

        /// <summary>English description / overview.</summary>
        public readonly string? Description;

        /// <summary>Gallery image URLs (full size).</summary>
        public readonly IEnumerable<string> GalleryImages;

        /// <summary>Initializes a new instance of the <see cref="Video" /> struct.</summary>
        public Video(
            string id,
            string code,
            string title,
            string? titleJa,
            IEnumerable<string> actresses,
            IEnumerable<string> genres,
            string? studio,
            string? cover,
            string? coverThumb,
            DateTime? releaseDate,
            int? runtimeMinutes = null,
            string? director = null,
            string? series = null,
            string? description = null,
            IEnumerable<string>? galleryImages = null)
        {
            this.Id = id;
            this.Code = code;
            this.Title = title;
            this.TitleJa = titleJa;
            this.Actresses = actresses;
            this.Genres = genres;
            this.Studio = studio;
            this.Cover = cover;
            this.CoverThumb = coverThumb;
            this.ReleaseDate = releaseDate;
            this.RuntimeMinutes = runtimeMinutes;
            this.Director = director;
            this.Series = series;
            this.Description = description;
            this.GalleryImages = galleryImages ?? Enumerable.Empty<string>();
        }

        /// <summary>Checks if two Video objects are equal.</summary>
        public static bool operator ==(Video v1, Video v2) => v1.Id == v2.Id && v1.Code == v2.Code;

        /// <summary>Checks if two Video objects are not equal.</summary>
        public static bool operator !=(Video v1, Video v2) => !(v1 == v2);

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Id: {this.Id}\n" +
                   $"Code: {this.Code}\n" +
                   $"Title: {this.Title}\n" +
                   $"TitleJa: {this.TitleJa}\n" +
                   $"Actresses: {string.Join(", ", this.Actresses)}\n" +
                   $"Genres: {string.Join(", ", this.Genres)}\n" +
                   $"Studio: {this.Studio}\n" +
                   $"Cover: {this.Cover}\n" +
                   $"CoverThumb: {this.CoverThumb}\n" +
                   $"ReleaseDate: {this.ReleaseDate}\n" +
                   $"RuntimeMinutes: {this.RuntimeMinutes}\n" +
                   $"Director: {this.Director}\n" +
                   $"Series: {this.Series}\n" +
                   $"Description: {this.Description}\n" +
                   $"GalleryImages: {this.GalleryImages.Count()}\n";
        }

        /// <inheritdoc />
        public override int GetHashCode() => this.Id.GetHashCode() ^ this.Code.GetHashCode();

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Video o && this == o;
    }
}
