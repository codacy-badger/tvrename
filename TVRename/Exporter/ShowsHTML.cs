// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
//

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using JetBrains.Annotations;

namespace TVRename
{
    // ReSharper disable once InconsistentNaming
    internal class ShowsHTML : ShowsExporter
    {
        public ShowsHTML(List<ShowConfiguration> shows) : base(shows)
        {
        }

        public override bool Active() => TVSettings.Instance.ExportShowsHTML;
        protected override string Location() => TVSettings.Instance.ExportShowsHTMLTo;

        protected override void Do()
        {
            using (System.IO.StreamWriter file = new System.IO.StreamWriter(Location()))
            {
                file.WriteLine(ShowHtmlHelper.HTMLHeader(8, Color.White));
                foreach (ShowConfiguration si in Shows)
                {
                    try
                    {
                        file.WriteLine(CreateHtml(si));
                    }
                    catch (Exception ex)
                    {
                        LOGGER.Error(ex,
                            $"Skipped adding {si.ShowName} to the outpur HTML as it is missing some data. Please try checking the settings and doing a force refresh on the show.");
                    }
                }

                file.WriteLine(ShowHtmlHelper.HTMLFooter());
            }
        }

        [NotNull]
        private static string CreateHtml([NotNull] ShowConfiguration si)
        {
            CachedSeriesInfo cachedSeries = si.CachedShow;
            if (cachedSeries is null)
            {
                return string.Empty;
            }

            string posterUrl = TheTVDB.API.GetImageURL(cachedSeries.GetImage(TVSettings.FolderJpgIsType.Poster));
            string yearRange = ShowHtmlHelper.YearRange(cachedSeries);
            string episodeSummary = cachedSeries.Episodes.Count.ToString();
            string stars = ShowHtmlHelper.StarRating(cachedSeries.SiteRating/2);
            string genreIcons = string.Join("&nbsp;", cachedSeries.Genres.Select(ShowHtmlHelper.GenreIconHtml));
            string siteRating = cachedSeries.SiteRating > 0 ? cachedSeries.SiteRating + "/10" : "";

            return $@"<div class=""card card-body"">
            <div class=""row"">
            <div class=""col-md-4"">
                <img class=""show-poster rounded w-100"" src=""{posterUrl}"" alt=""{si.ShowName} Show Poster""></div>
            <div class=""col-md-8 d-flex flex-column"">
                <div class=""row"">
                    <div class=""col-md-8""><h1>{si.ShowName}</h1></div>
                    <div class=""col-md-4 text-right""><h6>{yearRange} ({cachedSeries.Status})</h6><small class=""text-muted"">{episodeSummary} Episodes</small></div>
                </div>
            <div><blockquote>{cachedSeries.Overview}</blockquote></div>
            <div><blockquote>{cachedSeries.GetActorNames().ToCsv()}</blockquote></div>
            <div class=""row align-items-bottom flex-grow-1"">
                <div class=""col-md-4 align-self-end"">{stars}<br>{siteRating}</div>
                <div class=""col-md-4 align-self-end text-center"">{cachedSeries.ContentRating}<br>{cachedSeries.Network}</div>
                <div class=""col-md-4 align-self-end text-right"">{genreIcons}<br>{cachedSeries.Genres.ToCsv()}</div>
            </div>
            </div></div></div>";
        }
    }

    internal class MoviesHtml : MoviesExporter
    {
        public MoviesHtml(List<MovieConfiguration> shows) : base(shows)
        {
        }

        public override bool Active() => TVSettings.Instance.ExportMoviesHTML;
        protected override string Location() => TVSettings.Instance.ExportMoviesHTMLTo;

        protected override void Do()
        {
            using (System.IO.StreamWriter file = new System.IO.StreamWriter(Location()))
            {
                file.WriteLine(ShowHtmlHelper.HTMLHeader(8, Color.White));
                foreach (MovieConfiguration si in Shows)
                {
                    try
                    {
                        file.WriteLine(CreateHtml(si));
                    }
                    catch (Exception ex)
                    {
                        LOGGER.Error(ex,
                            $"Skipped adding {si.ShowName} to the outpur HTML as it is missing some data. Please try checking the settings and doing a force refresh on the show.");
                    }
                }

                file.WriteLine(ShowHtmlHelper.HTMLFooter());
            }
        }

        [NotNull]
        private static string CreateHtml([NotNull] MovieConfiguration si)
        {
            CachedMovieInfo cachedSeries = si.CachedMovie;
            if (cachedSeries is null)
            {
                return string.Empty;
            }

            string yearRange = cachedSeries.Year?.ToString() ?? "";
            string stars = ShowHtmlHelper.StarRating(cachedSeries.SiteRating / 2);
            string genreIcons = string.Join("&nbsp;", cachedSeries.Genres.Select(ShowHtmlHelper.GenreIconHtml));
            string siteRating = cachedSeries.SiteRating > 0 ? cachedSeries.SiteRating + "/10" : "";

            string poster = ShowHtmlHelper.CreatePosterHtml(cachedSeries);
            string runTimeHtml = string.IsNullOrWhiteSpace(cachedSeries.Runtime) ? string.Empty : $"<br/> {cachedSeries.Runtime} min";

            return $@"<div class=""card card-body"">
            <div class=""row"">
            <div class=""col-md-4"">
                {poster}
</div>
            <div class=""col-md-8 d-flex flex-column"">
                <div class=""row"">
                    <div class=""col-md-8""><h1>{si.ShowName}</h1><small class=""text-muted"">{cachedSeries.TagLine}</small></div>
                    <div class=""col-md-4 text-right""><h6>{yearRange} ({cachedSeries.Status})</h6>
<small class=""text-muted"">{cachedSeries.ShowLanguage} - {cachedSeries.Type}</small>
                        <small class=""text-muted"">{runTimeHtml}</small></div>
</div>
                
            <div><blockquote>{cachedSeries.Overview}</blockquote></div>
            <div><blockquote>{cachedSeries.GetActorNames().ToCsv()}</blockquote></div>
            <div class=""row align-items-bottom flex-grow-1"">
                <div class=""col-md-4 align-self-end"">{stars}<br>{siteRating}{ShowHtmlHelper.AddRatingCount(cachedSeries.SiteRatingVotes)}</div>
                <div class=""col-md-4 align-self-end text-center"">{cachedSeries.ContentRating}<br>{cachedSeries.Network}</div>
                <div class=""col-md-4 align-self-end text-right"">{genreIcons}<br>{cachedSeries.Genres.ToCsv()}</div>
            </div>
            </div></div></div>";
        }
    }
}
