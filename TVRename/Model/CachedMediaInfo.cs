using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace TVRename
{
    public abstract class CachedMediaInfo
    {
        public string Name;
        public string? Overview;
        public string? Runtime;
        public string? ContentRating;
        public float SiteRating;
        public int SiteRatingVotes;
        public string? Imdb;
        public int TvdbCode;
        public int TvMazeCode;
        public int TvRageCode;
        public int TmdbCode;
        public string? WebUrl;
        public string? OfficialUrl;
        public string? TrailerUrl;
        public string? ShowLanguage;
        public string? PosterUrl;
        public string? TwitterId;
        public string? InstagramId;
        public string? FacebookId;
        public string? TagLine;
        public string? SeriesId;
        public string? Slug;
        public double? Popularity;
        public DateTime? FirstAired;
        public Locale? ActualLocale; //The actual language obtained

        public string? Status { get; set; }
        public bool IsSearchResultOnly; // set to true if local info is known to be just certain fields found from search results. Do not need to be saved

        protected List<Actor> Actors;
        protected List<Crew> Crew;
        public List<string> Genres;
        protected List<string> Aliases;

        public bool Dirty; // set to true if local info is known to be older than whats on the server
        public long SrvLastUpdated;

        private protected static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();

        protected CachedMediaInfo(Locale locale)
        {
            Defaults();
            ActualLocale = locale;
        }

        private void Defaults()
        {
            Actors = new List<Actor>();
            Crew = new List<Crew>();
            Aliases = new List<string>();
            Genres = new List<string>();

            Dirty = false;
            Name = string.Empty;

            TvdbCode = -1;
            TvMazeCode = -1;
            TvRageCode = 0;
            TmdbCode = -1;

            Status = "Unknown";
        }

        protected CachedMediaInfo(int tvdb, int tvmaze, int tmdbId, Locale locale) : this(locale)
        {
            IsSearchResultOnly = false;
            TvMazeCode = tvmaze;
            TvdbCode = tvdb;
            TmdbCode = tmdbId;
            Defaults();
        }

        protected CachedMediaInfo()
        {
            Defaults();
        }

        protected abstract MediaConfiguration.MediaType MediaType();

        public int IdCode(TVDoc.ProviderType source)
        {
            return source switch
            {
                TVDoc.ProviderType.libraryDefault => IdCode(MediaType() == MediaConfiguration.MediaType.movie
                    ? TVSettings.Instance.DefaultMovieProvider
                    : TVSettings.Instance.DefaultProvider),
                TVDoc.ProviderType.TVmaze => TvMazeCode,
                TVDoc.ProviderType.TheTVDB => TvdbCode,
                TVDoc.ProviderType.TMDB => TmdbCode,
                _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
            };
        }

        public IEnumerable<string> GetAliases() => Aliases;

        public IEnumerable<Actor> GetActors() => Actors;

        [NotNull]
        public IEnumerable<string> GetActorNames() => GetActors().Select(x => x.ActorName);

        public void ClearActors()
        {
            Actors = new List<Actor>();
        }

        public void AddActor(Actor actor)
        {
            Actors.Add(actor);
        }

        public IEnumerable<Crew> GetCrew() => Crew;

        [NotNull]
        public IEnumerable<string> GetCrewNames() => GetCrew().Select(x => x.Name);

        public void ClearCrew()
        {
            Crew = new List<Crew>();
        }

        public void AddCrew(Crew crew)
        {
            Crew.Add(crew);
        }

        protected static float GetSiteRating([NotNull] XElement seriesXml)
        {
            string siteRatingString = seriesXml.ExtractStringOrNull("siteRating") ?? seriesXml.ExtractString("SiteRating");
            float.TryParse(siteRatingString,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.CreateSpecificCulture("en-US"), out float x);

            return x;
        }

        [NotNull]
        protected string GenerateErrorMessage() => "Error processing data from TheTVDB for a show. " + this + "\r\nLanguage: \"" + ActualLocale?.PreferredLanguage?.EnglishName + "\"";

        protected void LoadActors([NotNull] XElement seriesXml)
        {
            ClearActors();
            foreach (Actor a in seriesXml.Descendants("Actors").Descendants("Actor").Select(actorXml => new Actor(actorXml)))
            {
                AddActor(a);
            }
        }

        protected void LoadAliases([NotNull] XElement seriesXml)
        {
            Aliases = new List<string>();
            foreach (XElement aliasXml in seriesXml.Descendants("Aliases").Descendants("Alias"))
            {
                Aliases.Add(aliasXml.Value);
            }
        }

        protected void LoadGenres([NotNull] XElement seriesXml)
        {
            Genres = seriesXml
                .Descendants("Genres")
                .Descendants("Genre")
                .Select(g => g.Value.Trim()).Distinct()
                .ToList();
        }

        [NotNull]
        public string GetImdbNumber() =>
            Imdb is null ? string.Empty
            : Imdb.StartsWith("tt", StringComparison.Ordinal) ? Imdb.RemoveFirst(2)
            : Imdb;

        public void AddAlias(string s)
        {
            Aliases.Add(s);
        }

        public override string ToString() => $"TMDB:{TmdbCode}/TVDB:{TvdbCode}/Maze:{TvMazeCode}/{Name}";

        public void UpgradeSearchResultToDirty()
        {
            if (IsSearchResultOnly)
            {
                Dirty = true;
                IsSearchResultOnly = false;
            }
        }

        protected static string ChooseBetter(string? encumbant, bool betterLanguage, string? newValue)
        {
            if (string.IsNullOrEmpty(encumbant))
            {
                return newValue?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(newValue))
            {
                return encumbant.Trim();
            }

            return betterLanguage ? newValue.Trim() : encumbant.Trim();
        }

        [NotNull]
        protected static string ChooseBetterStatus(string? encumbant, bool betterLanguage, string? newValue)
        {
            if (string.IsNullOrEmpty(encumbant) || encumbant.Equals("Unknown"))
            {
                return newValue?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(newValue) || newValue.Equals("Unknown"))
            {
                return encumbant.Trim();
            }

            return betterLanguage ? newValue.Trim() : encumbant.Trim();
        }
    }
}
