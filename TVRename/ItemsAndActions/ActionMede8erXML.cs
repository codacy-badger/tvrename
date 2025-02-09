//
// Main website for TVRename is http://tvrename.com
//
// Source code available at https://github.com/TV-Rename/tvrename
//
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
//

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JetBrains.Annotations;

namespace TVRename
{
    using System;
    using System.Xml;
    using FileInfo = Alphaleonis.Win32.Filesystem.FileInfo;

    // ReSharper disable once InconsistentNaming
    public class ActionMede8erXML : ActionWriteMetadata
    {
        public ActionMede8erXML(FileInfo nfo, [NotNull] ProcessedEpisode pe) : base(nfo, pe.Show)
        {
            Episode = pe;
        }

        public ActionMede8erXML(FileInfo nfo, ShowConfiguration si) : base(nfo, si)
        {
            Episode = null;
        }

        #region Action Members

        [NotNull]
        public override string Name => "Write Mede8er Metadata";

        [NotNull]
        public override ActionOutcome Go(TVRenameStats stats)
        {
            try
            {
                if (Episode != null) // specific episode
                {
                    WriteEpisodeXml();
                }
                else if (SelectedShow != null)// show overview (Series.xml)
                {
                    WriteSeriesXml();
                }
                else if (Movie != null)// show overview (Series.xml)
                {
                    //WriteMovieXml(); TODO Mede8er support for movies
                }

                return ActionOutcome.Success();
            }
            catch (Exception e)
            {
                return new ActionOutcome(e);
            }
        }

        private void WriteEpisodeXml()
        {
            if (Episode == null)
            {
                return;
            }

            XmlWriterSettings settings = new() { Indent = true, NewLineOnAttributes = true };
            using (XmlWriter writer = XmlWriter.Create(Where.FullName, settings))
            {
                // See: http://xbmc.org/wiki/?title=Import_-_Export_Library#TV_Episodes
                writer.WriteStartElement("details");
                writer.WriteStartElement("movie");

                writer.WriteElement("title", Episode.Name);
                writer.WriteElement("season", Episode.AppropriateSeasonNumber);
                writer.WriteElement("episode", Episode.AppropriateEpNum);
                writer.WriteStartElement("year");
                if (Episode.FirstAired != null)
                {
                    writer.WriteValue(Episode.FirstAired.Value.ToString("yyyy"));
                }

                writer.WriteEndElement();

                //Mede8er Ratings are on a 100 point scale; TVDB are on a 10 point scale
                float siteRating = float.Parse(Episode.EpisodeRating ?? string.Empty, new CultureInfo("en-US")) * 10;
                int intSiteRating = (int)siteRating;
                if (intSiteRating > 0)
                {
                    writer.WriteElement("rating", intSiteRating);
                }

                //Get the Series OverView
                string sov = Episode.Show.CachedShow?.Overview;
                if (!string.IsNullOrEmpty(sov))
                {
                    writer.WriteElement("plot", sov);
                }

                //Get the Episode overview
                writer.WriteElement("episodeplot", Episode.Overview);
                writer.WriteElement("mpaa", Episode.Show.CachedShow?.ContentRating);

                //Runtime...taken from overall Series, not episode specific due to thetvdb
                string rt = Episode.Show.CachedShow?.Runtime;
                if (!string.IsNullOrEmpty(rt))
                {
                    writer.WriteElement("runtime", rt + " min");
                }

                //Genres...taken from overall Series, not episode specific due to thetvdb
                writer.WriteStartElement("genres");
                string genre = string.Join(" / ", Episode.Show.CachedShow?.Genres ?? new List<string>());
                if (!string.IsNullOrEmpty(genre))
                {
                    writer.WriteElement("genre", genre);
                }

                writer.WriteEndElement(); // genres

                //Director(s)
                if (!string.IsNullOrEmpty(Episode.EpisodeDirector))
                {
                    string epDirector = Episode.EpisodeDirector;
                    if (!string.IsNullOrEmpty(epDirector))
                    {
                        foreach (string daa in epDirector.Split('|').Where(daa => !string.IsNullOrEmpty(daa)))
                        {
                            writer.WriteElement("director", daa);
                        }
                    }
                }

                //Writers(s)
                if (!string.IsNullOrEmpty(Episode.Writer))
                {
                    string epWriter = Episode.Writer;
                    if (!string.IsNullOrEmpty(epWriter))
                    {
                        writer.WriteElement("credits", epWriter);
                    }
                }

                writer.WriteStartElement("cast");

                // actors...
                foreach (string aa in (Episode.Show.CachedShow?.GetActorNames() ?? new string[] { }).Where(
                    aa =>
                        !string.IsNullOrEmpty(aa)))
                {
                    writer.WriteElement("actor", aa);
                }

                writer.WriteEndElement(); // cast
                writer.WriteEndElement(); // movie
                writer.WriteEndElement(); // details
            }
        }

        private void WriteSeriesXml()
        {
            XmlWriterSettings settings = new() { Indent = true, NewLineOnAttributes = true };
            using (XmlWriter writer = XmlWriter.Create(Where.FullName, settings))
            {
                // http://www.xbmc.org/wiki/?title=Import_-_Export_Library#TV_Shows
                writer.WriteStartElement("details");
                writer.WriteStartElement("movie");
                writer.WriteElement("title", SelectedShow!.ShowName);

                writer.WriteStartElement("genres");
                string genre = string.Join(" / ", SelectedShow.CachedShow?.Genres ?? new List<string>());
                if (!string.IsNullOrEmpty(genre))
                {
                    writer.WriteElement("genre", genre);
                }

                writer.WriteEndElement(); // genres
                writer.WriteElement("premiered", SelectedShow.CachedShow?.FirstAired);
                writer.WriteElement("year", SelectedShow.CachedShow?.Year);

                //Mede8er Ratings are on a 100 point scale; TVDB are on a 10 point scale
                float siteRating = SelectedShow.CachedShow?.SiteRating ?? 0 * 10;
                int intSiteRating = (int)siteRating;
                if (intSiteRating > 0)
                {
                    writer.WriteElement("rating", intSiteRating);
                }

                writer.WriteElement("status", SelectedShow.CachedShow?.Status);
                writer.WriteElement("mpaa", SelectedShow.CachedShow?.ContentRating);
                writer.WriteInfo("moviedb", "imdb", "id", SelectedShow.CachedShow?.Imdb);
                writer.WriteElement("tvdbid", SelectedShow.CachedShow?.TvdbCode);
                string rt = SelectedShow.CachedShow?.Runtime;
                if (!string.IsNullOrEmpty(rt))
                {
                    writer.WriteElement("runtime", rt + " min");
                }

                writer.WriteElement("plot", SelectedShow.CachedShow?.Overview);
                writer.WriteStartElement("cast");

                // actors...
                foreach (string aa in
                    SelectedShow.CachedShow?.GetActorNames().Where(aa => !string.IsNullOrEmpty(aa)) ??
                    new List<string>())
                {
                    writer.WriteElement("actor", aa);
                }

                writer.WriteEndElement(); // cast
                writer.WriteEndElement(); // movie
                writer.WriteEndElement(); // tvshow
            }
        }

        #endregion Action Members

        #region Item Members

        public override bool SameAs(Item o)
        {
            return o is ActionMede8erXML xml && xml.Where == Where;
        }

        public override int CompareTo(Item o)
        {
            if (!(o is ActionMede8erXML nfo))
            {
                return -1;
            }

            if (Episode is null && nfo.Episode is null)
            {
                return string.Compare(Where.FullName, nfo.Where.FullName, StringComparison.Ordinal);
            }

            if (Episode is null)
            {
                return 1;
            }

            if (nfo.Episode is null)
            {
                return -1;
            }

            return string.Compare(Where.FullName + Episode.Name, nfo.Where.FullName + nfo.Episode.Name, StringComparison.Ordinal);
        }

        #endregion Item Members
    }
}
