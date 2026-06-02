namespace JellyfinJav.Providers.R18Provider
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Jellyfin.Data.Enums;
    using MediaBrowser.Controller.Collections;
    using MediaBrowser.Controller.Entities;
    using MediaBrowser.Controller.Entities.Movies;
    using MediaBrowser.Controller.Library;
    using MediaBrowser.Model.Entities;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Runs after every library scan and groups multi-part movies that share the same R18 content ID
    /// into Jellyfin box-set collections.
    /// </summary>
    public class R18CollectionTask : ILibraryPostScanTask
    {
        private readonly ILibraryManager libraryManager;
        private readonly ICollectionManager collectionManager;
        private readonly ILogger<R18CollectionTask> logger;

#pragma warning disable SA1614
        /// <summary>Initializes a new instance of the <see cref="R18CollectionTask"/> class.</summary>
        /// <param name="libraryManager"></param>
        /// <param name="collectionManager"></param>
        /// <param name="logger"></param>
        public R18CollectionTask(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            ILogger<R18CollectionTask> logger)
#pragma warning restore SA1614
        {
            this.libraryManager = libraryManager;
            this.collectionManager = collectionManager;
            this.logger = logger;
        }

        /// <inheritdoc />
        public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (Plugin.Instance?.Configuration.EnableCollectionGrouping != true)
            {
                this.logger.LogDebug("[JellyfinJav] R18 collection grouping is disabled, skipping.");
                progress.Report(100);
                return;
            }

            this.logger.LogInformation("[JellyfinJav] R18 collection grouping task started.");

            // All movies that have an R18 provider ID set.
            var allMovies = this.libraryManager
                .GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                })
                .OfType<Movie>()
                .Where(m => !string.IsNullOrEmpty(m.GetProviderId("R18")))
                .ToList();

            this.logger.LogInformation($"[JellyfinJav] Found {allMovies.Count} movie(s) with R18 IDs.");

            // Movies sharing the same R18 content ID are parts of the same title.
            var groups = allMovies
                .GroupBy(m => m.GetProviderId("R18"))
                .Where(g => g.Count() > 1)
                .ToList();

            this.logger.LogInformation($"[JellyfinJav] Found {groups.Count} multi-part group(s) to process.");

            double step = groups.Count > 0 ? 100.0 / groups.Count : 100.0;
            double current = 0;

            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var parts = group.ToList();

                // All parts share the same metadata title — use it as the collection name.
                string collectionName = parts[0].Name;

                this.logger.LogInformation(
                    $"[JellyfinJav] Processing collection \"{collectionName}\" ({parts.Count} parts, R18 ID: {group.Key}).");

                try
                {
                    var existing = this.libraryManager
                        .GetItemList(new InternalItemsQuery
                        {
                            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                            Name = collectionName,
                        })
                        .OfType<BoxSet>()
                        .FirstOrDefault();

                    if (existing != null)
                    {
                        this.logger.LogInformation(
                            $"[JellyfinJav] Adding parts to existing collection \"{collectionName}\".");
                        await this.collectionManager
                            .AddToCollectionAsync(existing.Id, parts.Select(p => p.Id))
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        this.logger.LogInformation(
                            $"[JellyfinJav] Creating new collection \"{collectionName}\".");
                        await this.collectionManager
                            .CreateCollectionAsync(new CollectionCreationOptions
                            {
                                Name = collectionName,
                                ItemIdList = parts.Select(p => p.Id.ToString("N")).ToArray(),
                            })
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, $"[JellyfinJav] Failed to create/update collection \"{collectionName}\".");
                }

                current += step;
                progress.Report(current);
            }

            progress.Report(100);
            this.logger.LogInformation("[JellyfinJav] R18 collection grouping task completed.");
        }
    }
}
