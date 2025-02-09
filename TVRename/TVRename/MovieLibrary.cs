using System;
using JetBrains.Annotations;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using NLog.Targets.Syslog.Settings;

namespace TVRename
{
    public class MovieLibrary : SafeList<MovieConfiguration>
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        [NotNull]
        public IEnumerable<MovieConfiguration> Movies => this;

        [NotNull]
        public List<(int, string)> Collections => Movies
            .Select(c => (c.CachedMovie?.CollectionId, c.CachedMovie?.CollectionName))
            .Where(a => a.CollectionId.HasValue && a.CollectionName.HasValue())
            .Select(a => (a.CollectionId.Value, a.CollectionName)).Distinct().ToList();

        [NotNull]
        public IEnumerable<string> GetGenres()
        {
            List<string> allGenres = new();
            foreach (MovieConfiguration si in Movies)
            {
                allGenres.AddRange(si.Genres);
            }

            List<string> distinctGenres = allGenres.Distinct().ToList();
            distinctGenres.Sort();
            return distinctGenres;
        }

        public MovieConfiguration? GetMovie(int id, TVDoc.ProviderType provider)
        {
            if (id == 0 || id == -1)
            {
                return null;
            }
            List<MovieConfiguration> matching = Movies.Where(configuration => configuration.IdFor(provider) == id).ToList();

            if (!matching.Any())
            {
                return null;
            }
            if (matching.Count == 1)
            {
                return matching.First();
            }
            throw new InvalidOperationException(
                $"Searched for {id} on {provider.PrettyPrint()} Movie Library has multiple: {matching.Select(x => x.ToString()).ToCsv()}");
    }

    public new void AddMovie(MovieConfiguration newShow,bool showErrors)
        {
            if (Contains(newShow))
            {
                return;
            }

            List<MovieConfiguration> matchingShows = Movies.Where(configuration => configuration.AnyIdsMatch(newShow)).ToList();
            if (matchingShows.Any())
            {
                foreach (MovieConfiguration existingshow in matchingShows)
                {
                    //TODO Merge them in
                    existingshow.AutomaticFolderRoot = newShow.AutomaticFolderRoot;

                    if (showErrors)
                    {
                        Logger.Error($"Trying to add {newShow}, but we already have {existingshow}");
                        Logger.Error(Environment.StackTrace);
                    }
                    else
                    {
                        Logger.Warn($"Trying to add {newShow}, but we already have {existingshow}");
                    }
                }
                return;
            }

            Add(newShow);
        }
        public void AddMovies(List<MovieConfiguration>? newMovie, bool showErrors)
        {
            if (newMovie is null)
            {
                return;
            }

            foreach (MovieConfiguration toAdd in newMovie)
            {
                AddMovie(toAdd,showErrors);
            }
        }

        public void LoadFromXml(XElement? xmlSettings)
        {
            if (xmlSettings != null)
            {
                foreach (MovieConfiguration si in xmlSettings.Descendants("MovieItem").Select(showSettings => new MovieConfiguration(showSettings)))
                {
                    if (si.UseCustomShowName) // see if custom show name is actually the real show name
                    {
                        CachedMovieInfo? ser = si.CachedMovie;
                        if (ser != null && si.CustomShowName == ser.Name)
                        {
                            // then, turn it off
                            si.CustomShowName = string.Empty;
                            si.UseCustomShowName = false;
                        }
                    }

                    AddMovie(si,false);
                }
            }
        }

        [NotNull]
        public List<MovieConfiguration> GetSortedMovies()
        {
            List<MovieConfiguration> returnList = Movies.ToList();
            returnList.Sort(MediaConfiguration.CompareNames);
            return returnList;
        }

        [NotNull]
        public IEnumerable<string> GetNetworks()
        {
            return Movies
                .Select(si => si.CachedMovie)
                .Where(seriesInfo => !string.IsNullOrWhiteSpace(seriesInfo?.Network))
                .SelectMany(seriesInfo => seriesInfo.Networks)
                .Distinct()
                .OrderBy(s => s);
        }

        [NotNull]
        public IEnumerable<string> GetContentRatings()
        {
            return Movies.Select(si => si.CachedMovie)
                .Where(s => !string.IsNullOrWhiteSpace(s?.ContentRating))
                .Select(s => s.ContentRating)
                .Distinct()
                .OrderBy(s => s);
        }

        [NotNull]
        public IEnumerable<string> GetYears()
        {
            return Movies.Select(si => si.CachedMovie?.Year)
                .Where(s => s.HasValue)
                .Select(s => s!.ToString())
                .Distinct()
                .OrderBy(s => s);
        }

        [NotNull]
        public IEnumerable<string> GetStatuses()
        {
            return Movies
                .Select(s => s.CachedMovie)
                .Where(s => !string.IsNullOrWhiteSpace(s?.Status))
                .Select(s => s.Status)
                .Distinct()
                .OrderBy(s => s);
        }

        public MovieConfiguration? GetMovie([NotNull] ISeriesSpecifier ai) => GetMovie(ai.Id(), ai.Provider);
    }
}
