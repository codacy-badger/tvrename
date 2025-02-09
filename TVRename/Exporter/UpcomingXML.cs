//
// Main website for TVRename is http://tvrename.com
//
// Source code available at https://github.com/TV-Rename/tvrename
//
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
//

using System;
using System.Collections.Generic;
using System.Xml;
using JetBrains.Annotations;
using FileInfo = Alphaleonis.Win32.Filesystem.FileInfo;

namespace TVRename
{
    // ReSharper disable once InconsistentNaming
    internal class UpcomingXML : UpcomingExporter
    {
        public UpcomingXML(TVDoc i) : base(i)
        {
        }

        public override bool Active() => TVSettings.Instance.ExportWTWXML;
        [NotNull]
        protected override string Name() => "Upcoming XML Exporter";
        protected override string Location() => TVSettings.Instance.ExportWTWXMLTo;

        protected override bool Generate([NotNull] System.IO.Stream str, [NotNull] IEnumerable<ProcessedEpisode> elist)
        {
            DirFilesCache dfc = new();
            XmlWriterSettings settings = new()
            {
                Indent = true,
                NewLineOnAttributes = true,
                Encoding = System.Text.Encoding.ASCII
            };
            using (XmlWriter writer = XmlWriter.Create(str, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("WhenToWatch");

                foreach (ProcessedEpisode ei in elist)
                {
                    writer.WriteStartElement("item");
                    writer.WriteElement("ShowTvdbId", ei.Show.TvdbCode);
                    writer.WriteElement("ShowTMDBId", ei.Show.TmdbCode);
                    writer.WriteElement("ShowTvMazeId", ei.Show.TVmazeCode);
                    writer.WriteElement("ShowIMDB", ei.Show.ImdbCode);
                    writer.WriteElement("SeriesName", ei.Show.Name);
                    writer.WriteElement("SeasonNumber", Helpers.Pad(ei.AppropriateSeasonNumber));
                    writer.WriteElement("EpisodeNumber", Helpers.Pad(ei.AppropriateEpNum));
                    writer.WriteElement("EpisodeName", ei.Name);

                    writer.WriteStartElement("available");
                    DateTime? airdt = ei.GetAirDateDt(true);

                    if (airdt.HasValue && airdt.Value.CompareTo(DateTime.Now) < 0) // has aired
                    {
                        List<FileInfo> fl = dfc.FindEpOnDisk(ei);
                        if (fl.Count > 0)
                        {
                            writer.WriteValue("true");
                        }
                        else if (ei.Show.DoMissingCheck)
                        {
                            writer.WriteValue("false");
                        }
                        else
                        {
                            writer.WriteValue("no missing check");
                        }
                    }
                    else
                    {
                        writer.WriteValue("future");
                    }

                    writer.WriteEndElement();
                    writer.WriteElement("Overview", ei.Overview);

                    writer.WriteStartElement("FirstAired");
                    DateTime? dt = ei.GetAirDateDt(true);
                    if (dt != null)
                    {
                        writer.WriteValue(dt.Value.ToString("F"));
                    }

                    writer.WriteEndElement();

                    writer.WriteElement("Rating", ei.EpisodeRating);
                    writer.WriteElement("filename", ei.ThumbnailUrl());

                    writer.WriteEndElement(); // item
                }
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            return true;
        }
    }
}
