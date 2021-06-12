//
// Main website for TVRename is http://tvrename.com
//
// Source code available at https://github.com/TV-Rename/tvrename
//
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
//
using Alphaleonis.Win32.Filesystem;
using JetBrains.Annotations;
using NodaTime;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using TimeZoneConverter;

// These are what is used when processing folders for missing episodes, renaming, etc. of files.

// A "ProcessedEpisode" is generated by processing an Episode from thetvdb, and merging/renaming/etc.
//
// A "ShowItem" is a show the user has added on the "My Shows" tab

namespace TVRename
{
    public class ShowConfiguration : MediaConfiguration
    {
        public string AutoAddFolderBase;
        public string AutoAddCustomFolderFormat;
        public AutomaticFolderType AutoAddType;

        public bool CountSpecials;
        public bool DvdOrder; // sort by DVD order, not the default sort we get
        public bool ForceCheckFuture;
        public bool ForceCheckNoAirdate;
        public readonly List<int> IgnoreSeasons;
        public readonly ConcurrentDictionary<int, List<string>> ManualFolderLocations;
        public readonly ConcurrentDictionary<int, List<ProcessedEpisode>> SeasonEpisodes; // built up by applying rules.
        private readonly ConcurrentDictionary<int, ProcessedSeason> airedSeasons;
        private readonly ConcurrentDictionary<int, ProcessedSeason> dvdSeasons;
        public readonly ConcurrentDictionary<int, List<ShowRule>> SeasonRules;
        public bool ShowNextAirdate;
        public bool UseSequentialMatch;
        public bool UseEpNameMatch;
        public bool UseAirDateMatch;
        public bool UseCustomSearchUrl;
        public string CustomSearchUrl;
        public bool UseCustomNamingFormat;
        public string CustomNamingFormat;
        public bool ManualFoldersReplaceAutomatic;

        public string ShowTimeZone { get; internal set; }
        private DateTimeZone? seriesTimeZone;
        private string lastFiguredTz;
        public DateTime? BannersLastUpdatedOnDisk { get; set; }
        public ProcessedSeason.SeasonType Order => DvdOrder ? ProcessedSeason.SeasonType.dvd : ProcessedSeason.SeasonType.aired;

        #region AutomaticFolderType enum

        public enum AutomaticFolderType
        {
            none,
            baseOnly,
            libraryDefault,
            custom
        }

        #endregion AutomaticFolderType enum

        public ShowConfiguration()
        {
            ManualFolderLocations = new ConcurrentDictionary<int, List<string>>();
            SeasonRules = new ConcurrentDictionary<int, List<ShowRule>>();
            SeasonEpisodes = new ConcurrentDictionary<int, List<ProcessedEpisode>>();
            airedSeasons = new ConcurrentDictionary<int, ProcessedSeason>();
            dvdSeasons = new ConcurrentDictionary<int, ProcessedSeason>();
            IgnoreSeasons = new List<int>();

            UseCustomRegion = false;
            CustomRegionCode = string.Empty;

            UseCustomShowName = false;
            CustomShowName = string.Empty;
            UseCustomLanguage = false;
            TvdbCode = -1;
            TVmazeCode = -1;
            TmdbCode = -1;
            UseCustomSearchUrl = false;
            CustomSearchUrl = string.Empty;
            UseCustomNamingFormat = false;
            CustomNamingFormat = string.Empty;
            ManualFoldersReplaceAutomatic = false;
            BannersLastUpdatedOnDisk = null; //assume that the banners are old and have expired
            ShowTimeZone = TVSettings.Instance.DefaultShowTimezoneName ?? TimeZoneHelper.DefaultTimeZone(); // default, is correct for most shows
            lastFiguredTz = string.Empty;

            UseSequentialMatch = TVSettings.Instance.DefShowSequentialMatching;
            UseAirDateMatch = TVSettings.Instance.DefShowAirDateMatching;
            UseEpNameMatch = TVSettings.Instance.DefShowEpNameMatching;
            ShowNextAirdate = TVSettings.Instance.DefShowNextAirdate;
            DoRename = TVSettings.Instance.DefShowDoRenaming;
            DoMissingCheck = TVSettings.Instance.DefShowDoMissingCheck;
            CountSpecials = TVSettings.Instance.DefShowSpecialsCount;
            DvdOrder = TVSettings.Instance.DefShowDVDOrder;
            ForceCheckNoAirdate = TVSettings.Instance.DefShowIncludeNoAirdate;
            ForceCheckFuture = TVSettings.Instance.DefShowIncludeFuture;

            AutoAddCustomFolderFormat = CustomSeasonName.DefaultStyle();

            AutoAddFolderBase =
                  !TVSettings.Instance.DefShowAutoFolders ? string.Empty
                : !TVSettings.Instance.DefShowUseDefLocation ? string.Empty
                : TVSettings.Instance.DefShowLocation.EnsureEndsWithSeparator() + TVSettings.Instance.FilenameFriendly(FileHelper.MakeValidPath(ShowName));

            AutoAddType =
                !TVSettings.Instance.DefShowAutoFolders ? AutomaticFolderType.none
                : TVSettings.Instance.DefShowUseBase ? AutomaticFolderType.baseOnly
                : AutomaticFolderType.libraryDefault;
        }

        public ShowConfiguration(int code, TVDoc.ProviderType type) : this()
        {
            ConfigurationProvider = type;
            SetId(type, code);
        }

        public void AddEpisode([NotNull] Episode e)
        {
            ProcessedSeason airedProcessedSeason = GetOrAddAiredSeason(e.AiredSeasonNumber, e.SeasonId);
            airedProcessedSeason.AddUpdateEpisode(e);

            ProcessedSeason dvdProcessedSeason = GetOrAddDvdSeason(e.DvdSeasonNumber, e.SeasonId);
            dvdProcessedSeason.AddUpdateEpisode(e);
        }

        [NotNull]
        public string ShowStatus => CachedShow?.Status ?? "Unknown";

        public ProcessedSeason GetOrAddAiredSeason(int num, int seasonId)
        {
            if (airedSeasons.ContainsKey(num))
            {
                return airedSeasons[num];
            }

            ProcessedSeason s = new ProcessedSeason(this, num, seasonId, ProcessedSeason.SeasonType.aired);
            airedSeasons[num] = s;

            return s;
        }

        public ProcessedSeason GetOrAddDvdSeason(int num, int seasonId)
        {
            if (dvdSeasons.ContainsKey(num))
            {
                return dvdSeasons[num];
            }

            ProcessedSeason s = new ProcessedSeason(this, num, seasonId, ProcessedSeason.SeasonType.dvd);
            dvdSeasons[num] = s;

            return s;
        }

        public int GetSeasonIndex(int seasonNumber)
        {
            List<int> seasonNumbers = AppropriateSeasons().Values.ToList().Where(season => !season.IsSpecial).Select(sn => sn.SeasonNumber).ToList();

            seasonNumbers.Sort();

            return seasonNumbers.IndexOf(seasonNumber) + 1;
        }

        [Serializable]
        public class EpisodeNotFoundException : Exception
        {
        }

        private bool HasAnyAirdates(int snum)
        {
            ConcurrentDictionary<int, ProcessedSeason> seasonsToUse = AppropriateSeasons();

            return seasonsToUse.ContainsKey(snum) && seasonsToUse[snum].Episodes.Values.Any(e => e.FirstAired != null);
        }

        //todo use this function MS_SI_CHANGE
        internal void RemoveEpisode(int episodeId)
        {
            //Remove from Aired and DVD Seasons
            dvdSeasons.Values.ForEach(s => s.RemoveEpisode(episodeId));
            airedSeasons.Values.ForEach(s => s.RemoveEpisode(episodeId));
        }

        [NotNull]
        internal ProcessedEpisode GetEpisode(int seasF, int epF)
        {
            if (!SeasonEpisodes.ContainsKey(seasF))
            {
                throw new EpisodeNotFoundException();
            }

            foreach (ProcessedEpisode pep in SeasonEpisodes[seasF])
            {
                if (pep.AppropriateEpNum == epF)
                {
                    return pep;
                }
            }

            throw new EpisodeNotFoundException();
        }

        public DateTime? LastAiredDate
        {
            get
            {
                DateTime? returnValue = null;
                foreach (ProcessedSeason s in airedSeasons.Values) //We can use AiredSeasons as it does not matter which order we do this in Aired or DVD
                {
                    DateTime? seasonLastAirDate = s.LastAiredDate();

                    if (!seasonLastAirDate.HasValue)
                    {
                        continue;
                    }

                    if (!returnValue.HasValue)
                    {
                        returnValue = seasonLastAirDate.Value;
                    }
                    else if (DateTime.Compare(seasonLastAirDate.Value, returnValue.Value) > 0)
                    {
                        returnValue = seasonLastAirDate.Value;
                    }
                }
                return returnValue;
            }
        }

        private DateTimeZone FigureOutTimeZone()
        {
            string tzstr = ShowTimeZone;

            if (string.IsNullOrEmpty(tzstr))
            {
                tzstr = TimeZoneHelper.DefaultTimeZone();
            }

            try
            {
                return DateTimeZoneProviders.Tzdb[tzstr];
            }
            catch (Exception ex)
            {
                LOGGER.Info($"Could not work out what timezone '{ShowName}' has. In the settings it uses '{tzstr}', Testing to see whether it needs to be upgraded: {ex.Message}");
                try
                {
                    tzstr = TZConvert.WindowsToIana(tzstr);
                    ShowTimeZone = tzstr;
                    lastFiguredTz = tzstr;
                    return DateTimeZoneProviders.Tzdb[tzstr];
                }
                catch (Exception ex2)
                {
                    LOGGER.Warn(ex2,
                        $"Could not work out what timezone '{ShowName}' has. In the settings it uses '{tzstr}', but that is not valid. Please update. Using the default timezone {TimeZoneHelper.DefaultTimeZone()} for the show instead.");

                    try
                    {
                        tzstr = TimeZoneHelper.DefaultTimeZone();
                        ShowTimeZone = tzstr;
                        lastFiguredTz = tzstr;
                        return DateTimeZoneProviders.Tzdb[tzstr];
                    }
                    catch (Exception ex3)
                    {
                        LOGGER.Warn(ex3,
                            $"Could not work out what timezone '{ShowName}' has. In the settings it uses '{tzstr}', but that is not valid. Tried to use the default timezone {TimeZoneHelper.DefaultTimeZone()} for the show instead - also invalid.  Please update.");

                        ShowTimeZone = DateTimeZoneProviders.Tzdb.GetSystemDefault().Id;
                        lastFiguredTz = tzstr;
                        return DateTimeZoneProviders.Tzdb.GetSystemDefault();
                    }
                }
            }
        }

        public DateTimeZone GetTimeZone()
        {
            if (seriesTimeZone is null || lastFiguredTz != ShowTimeZone)
            {
                seriesTimeZone = FigureOutTimeZone();
            }
            return seriesTimeZone;
        }

        public ShowConfiguration([NotNull] XElement xmlSettings) : this()
        {
            CustomShowName = xmlSettings.ExtractString("ShowName");
            UseCustomShowName = xmlSettings.ExtractBool("UseCustomShowName", false);
            UseCustomLanguage = xmlSettings.ExtractBool("UseCustomLanguage", false);
            CustomLanguageCode = xmlSettings.ExtractString("CustomLanguageCode");
            CustomShowName = xmlSettings.ExtractString("CustomShowName");
            TvdbCode = xmlSettings.ExtractInt("TVDBID", -1);
            TVmazeCode = xmlSettings.ExtractInt("TVMAZEID", -1);
            TmdbCode = xmlSettings.ExtractInt("TMDBID", -1);
            LastName = xmlSettings.ExtractStringOrNull("LastName");
            ImdbCode = xmlSettings.ExtractStringOrNull("ImdbCode");

            CountSpecials = xmlSettings.ExtractBool("CountSpecials", false);
            ShowNextAirdate = xmlSettings.ExtractBool("ShowNextAirdate", true);
            AutoAddFolderBase = xmlSettings.ExtractString("FolderBase");
            DoRename = xmlSettings.ExtractBool("DoRename", true);
            DoMissingCheck = xmlSettings.ExtractBool("DoMissingCheck", true);
            DvdOrder = xmlSettings.ExtractBool("DVDOrder", false);
            UseCustomSearchUrl = xmlSettings.ExtractBool("UseCustomSearchURL", false);
            CustomSearchUrl = xmlSettings.ExtractString("CustomSearchURL");
            UseCustomNamingFormat = xmlSettings.ExtractBool("UseCustomNamingFormat", false);
            CustomNamingFormat = xmlSettings.ExtractString("CustomNamingFormat");
            ShowTimeZone = xmlSettings.ExtractStringOrNull("TimeZone") ?? TimeZoneHelper.DefaultTimeZone(); // default, is correct for most shows
            ForceCheckFuture = xmlSettings.ExtractBoolBackupDefault("ForceCheckFuture", "ForceCheckAll", false);
            ForceCheckNoAirdate = xmlSettings.ExtractBoolBackupDefault("ForceCheckNoAirdate", "ForceCheckAll", false);
            AutoAddCustomFolderFormat = xmlSettings.ExtractStringOrNull("CustomFolderFormat") ?? CustomSeasonName.DefaultStyle();
            AutoAddType = GetAutoAddType(xmlSettings.ExtractInt("AutoAddType"));
            ConfigurationProvider = GetConfigurationProviderType(xmlSettings.ExtractInt("ConfigurationProvider"));

            BannersLastUpdatedOnDisk = xmlSettings.ExtractDateTime("BannersLastUpdatedOnDisk");
            UseSequentialMatch = xmlSettings.ExtractBool("UseSequentialMatch", false);
            UseAirDateMatch = xmlSettings.ExtractBool("UseAirDateMatch", false);
            UseEpNameMatch = xmlSettings.ExtractBool("UseEpNameMatch", false);
            ManualFoldersReplaceAutomatic = xmlSettings.ExtractBool("ManualFoldersReplaceAutomatic", false);

            SetupIgnoreRules(xmlSettings);
            SetupAliases(xmlSettings);
            SetupSeasonRules(xmlSettings);
            SetupSeasonFolders(xmlSettings);
            UpgradeFromOldSeasonFormat(xmlSettings);
        }

        private static AutomaticFolderType GetAutoAddType(int? value)
        {
            return value is null ? AutomaticFolderType.libraryDefault : (AutomaticFolderType)value;
        }

        private void UpgradeFromOldSeasonFormat([NotNull] XElement xmlSettings)
        {
            //These variables have been discontinued (JULY 2018).  If we have any then we should migrate to the new values
            bool upgradeFromOldAutoAddFunction = xmlSettings.Descendants("AutoAddNewSeasons").Any()
                                                 || xmlSettings.Descendants("FolderPerSeason").Any()
                                                 || xmlSettings.Descendants("SeasonFolderName").Any()
                                                 || xmlSettings.Descendants("PadSeasonToTwoDigits").Any();
            bool tempAutoAddNewSeasons = xmlSettings.ExtractBool("AutoAddNewSeasons", true);
            bool tempAutoAddFolderPerSeason = xmlSettings.ExtractBool("FolderPerSeason", true);
            string tempAutoAddSeasonFolderName = xmlSettings.ExtractString("SeasonFolderName");
            bool tempPadSeasonToTwoDigits = xmlSettings.ExtractBool("PadSeasonToTwoDigits", true);

            if (upgradeFromOldAutoAddFunction)
            {
                if (tempAutoAddNewSeasons)
                {
                    if (tempAutoAddFolderPerSeason)
                    {
                        AutoAddCustomFolderFormat = tempAutoAddSeasonFolderName + (tempPadSeasonToTwoDigits || TVSettings.Instance.LeadingZeroOnSeason ? "{Season:2}" : "{Season}");
                        AutoAddType = AutoAddCustomFolderFormat == TVSettings.Instance.SeasonFolderFormat
                            ? AutomaticFolderType.libraryDefault
                            : AutomaticFolderType.custom;
                    }
                    else
                    {
                        AutoAddCustomFolderFormat = string.Empty;
                        AutoAddType = AutomaticFolderType.baseOnly;
                    }
                }
                else
                {
                    AutoAddCustomFolderFormat = string.Empty;
                    AutoAddType = AutomaticFolderType.none;
                }
            }
        }

        private void SetupIgnoreRules([NotNull] XElement xmlSettings)
        {
            foreach (int seasonNumber in xmlSettings.Descendants("IgnoreSeasons").Descendants("Ignore").Select(ig => XmlConvert.ToInt32(ig.Value)).Distinct())
            {
                IgnoreSeasons.Add(seasonNumber);
            }
        }

        private void SetupSeasonRules([NotNull] XElement xmlSettings)
        {
            foreach (XElement rulesSet in xmlSettings.Descendants("Rules"))
            {
                XAttribute? value = rulesSet.Attribute("SeasonNumber");
                if (value is null)
                {
                    continue;
                }

                int snum = int.Parse(value.Value);
                SeasonRules[snum] = new List<ShowRule>();

                foreach (XElement ruleData in rulesSet.Descendants("Rule"))
                {
                    SeasonRules[snum].Add(new ShowRule(ruleData));
                }
            }
        }

        private void SetupSeasonFolders([NotNull] XElement xmlSettings)
        {
            foreach (XElement seasonFolder in xmlSettings.Descendants("SeasonFolders"))
            {
                XAttribute? value = seasonFolder.Attribute("SeasonNumber");
                if (value is null)
                {
                    continue;
                }

                int snum = int.Parse(value.Value);

                ManualFolderLocations[snum] = new List<string>();

                foreach (string ff in seasonFolder.Descendants("Folder")
                    .Select(folderData => folderData.Attribute("Location")?.Value)
                    .Distinct()
                    .Where(ff => !string.IsNullOrWhiteSpace(ff) && AutoFolderNameForSeason(snum) != ff))
                {
                    ManualFolderLocations[snum].Add(ff);
                }
            }
        }

        internal bool UsesManualFolders() => ManualFolderLocations.Count > 0;

        protected override MediaType GetMediaType() => MediaType.tv;

        protected override MediaCache LocalCache()
        {
            return LocalCache(Provider == TVDoc.ProviderType.libraryDefault ? TVSettings.Instance.DefaultProvider : Provider);
        }

        private static MediaCache LocalCache(TVDoc.ProviderType provider) => TVDoc.GetMediaCache(provider);

        public enum ShowAirStatus
        {
            noEpisodesOrSeasons,
            aired,
            partiallyAired,
            noneAired
        }

        public ShowAirStatus SeasonsAirStatus
        {
            get
            {
                if (!HasSeasonsAndEpisodes)
                {
                    return ShowAirStatus.noEpisodesOrSeasons;
                }

                if (HasAiredEpisodes && !HasUnairedEpisodes)
                {
                    return ShowAirStatus.aired;
                }

                if (HasUnairedEpisodes && !HasAiredEpisodes)
                {
                    return ShowAirStatus.noneAired;
                }

                if (HasAiredEpisodes && HasUnairedEpisodes)
                {
                    return ShowAirStatus.partiallyAired;
                }

                //System.Diagnostics.Debug.Assert(false, "That is weird ... we have 'seasons and episodes' but none are aired, nor unaired. That case shouldn't actually occur !");
                return ShowAirStatus.noEpisodesOrSeasons;
            }
        }

        private bool HasSeasonsAndEpisodes
        {
            get
            {
                //We can use AiredSeasons as it does not matter which order we do this in Aired or DVD
                CachedSeriesInfo? cachedSeriesInfo = (CachedSeriesInfo)CachedData;
                if (cachedSeriesInfo?.Episodes is null || cachedSeriesInfo.Episodes.Count <= 0)
                {
                    return false;
                }

                return true;
            }
        }

        private bool HasUnairedEpisodes
        {
            get
            {
                if (!HasSeasonsAndEpisodes)
                {
                    return false;
                }

                foreach (KeyValuePair<int, ProcessedSeason> s in airedSeasons)
                {
                    if (IgnoreSeasons.Contains(s.Key))
                    {
                        continue;
                    }

                    if (TVSettings.Instance.IgnoreAllSpecials && s.Key == 0)
                    {
                        continue;
                    }

                    if (s.Value.Status(GetTimeZone()) == ProcessedSeason.SeasonStatus.noneAired ||
                        s.Value.Status(GetTimeZone()) == ProcessedSeason.SeasonStatus.partiallyAired)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private bool HasAiredEpisodes
        {
            get
            {
                if (!HasSeasonsAndEpisodes)
                {
                    return false;
                }

                foreach (KeyValuePair<int, ProcessedSeason> s in airedSeasons)
                {
                    if (IgnoreSeasons.Contains(s.Key))
                    {
                        continue;
                    }

                    if (TVSettings.Instance.IgnoreAllSpecials && s.Key == 0)
                    {
                        continue;
                    }

                    if (s.Value.Status(GetTimeZone()) == ProcessedSeason.SeasonStatus.partiallyAired || s.Value.Status(GetTimeZone()) == ProcessedSeason.SeasonStatus.aired)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        [NotNull]
        public IEnumerable<KeyValuePair<int, List<ProcessedEpisode>>> ActiveSeasons
        {
            get
            {
                return SeasonEpisodes
                    .Where(pair => !IgnoreSeasons.Contains(pair.Key)) //not an ignored season
                    .Where(pair => !(pair.Key == 0 && TVSettings.Instance.IgnoreAllSpecials)) //not a specials where all specials are ignored
                    .Where(pair => !(pair.Key == 0 && CountSpecials)); //not a specials where this shows specials are ignored
            }
        }

        public string? WebsiteUrl
        {
            get
            {
                if (Provider == TVDoc.ProviderType.TheTVDB)
                {
                    return TheTVDB.API.WebsiteShowUrl(this);
                }
                if (Provider == TVDoc.ProviderType.TMDB)
                {
                    return TMDB.API.WebsiteShowUrl(this);
                }

                return CachedShow?.Slug;
            }
        }

        protected override TVDoc.ProviderType DefaultProvider() => TVSettings.Instance.DefaultProvider;

        public List<ShowRule>? RulesForSeason(int n)
        {
            return SeasonRules.ContainsKey(n) ? SeasonRules[n] : null;
        }

        [NotNull]
        private string AutoFolderNameForSeason(ProcessedSeason? s)
        {
            string r = AutoAddFolderBase;
            if (string.IsNullOrEmpty(r))
            {
                return string.Empty;
            }

            if (s is null)
            {
                return string.Empty;
            }

            if (!r.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                r += System.IO.Path.DirectorySeparatorChar.ToString();
            }

            if (AutoAddType == AutomaticFolderType.none)
            {
                return r;
            }

            if (AutoAddType == AutomaticFolderType.baseOnly)
            {
                return r;
            }

            if (s.IsSpecial)
            {
                return r + TVSettings.Instance.SpecialsFolderName;
            }

            if (AutoAddType == AutomaticFolderType.libraryDefault)
            {
                return r + CustomSeasonName.NameFor(s, TVSettings.Instance.SeasonFolderFormat);
            }

            if (AutoAddType == AutomaticFolderType.custom)
            {
                return r + CustomSeasonName.NameFor(s, AutoAddCustomFolderFormat);
            }

            return r;
        }

        public int MaxSeason()
        {
            int max = 0;
            foreach (KeyValuePair<int, List<ProcessedEpisode>> kvp in SeasonEpisodes)
            {
                if (kvp.Key > max)
                {
                    max = kvp.Key;
                }
            }
            return max;
        }

        public void WriteXmlSettings([NotNull] XmlWriter writer)
        {
            writer.WriteStartElement("ShowItem");

            writer.WriteElement("UseCustomShowName", UseCustomShowName);
            writer.WriteElement("CustomShowName", CustomShowName);
            writer.WriteElement("UseCustomLanguage", UseCustomLanguage);
            writer.WriteElement("CustomLanguageCode", CustomLanguageCode);
            writer.WriteElement("ShowNextAirdate", ShowNextAirdate);
            writer.WriteElement("TVDBID", TvdbCode);
            writer.WriteElement("TVMAZEID", TVmazeCode);
            writer.WriteElement("TMDBID", TmdbCode);
            writer.WriteElement("LastName", LastName);
            writer.WriteElement("ImdbCode", ImdbCode);
            writer.WriteElement("FolderBase", AutoAddFolderBase);
            writer.WriteElement("DoRename", DoRename);
            writer.WriteElement("DoMissingCheck", DoMissingCheck);
            writer.WriteElement("CountSpecials", CountSpecials);
            writer.WriteElement("DVDOrder", DvdOrder);
            writer.WriteElement("ForceCheckNoAirdate", ForceCheckNoAirdate);
            writer.WriteElement("ForceCheckFuture", ForceCheckFuture);
            writer.WriteElement("UseSequentialMatch", UseSequentialMatch);
            writer.WriteElement("UseAirDateMatch", UseAirDateMatch);
            writer.WriteElement("UseEpNameMatch", UseEpNameMatch);
            writer.WriteElement("CustomFolderFormat", AutoAddCustomFolderFormat);
            writer.WriteElement("AutoAddType", (int)AutoAddType);
            writer.WriteElement("ConfigurationProvider", (int)ConfigurationProvider);
            writer.WriteElement("BannersLastUpdatedOnDisk", BannersLastUpdatedOnDisk);
            writer.WriteElement("TimeZone", ShowTimeZone);
            writer.WriteElement("ManualFoldersReplaceAutomatic", ManualFoldersReplaceAutomatic);

            writer.WriteStartElement("IgnoreSeasons");
            foreach (int i in IgnoreSeasons)
            {
                writer.WriteElement("Ignore", i);
            }
            writer.WriteEndElement();

            writer.WriteStartElement("AliasNames");
            foreach (string str in AliasNames)
            {
                writer.WriteElement("Alias", str);
            }
            writer.WriteEndElement();

            writer.WriteElement("UseCustomSearchURL", UseCustomSearchUrl);
            writer.WriteElement("CustomSearchURL", CustomSearchUrl);

            writer.WriteElement("UseCustomNamingFormat", UseCustomNamingFormat);
            writer.WriteElement("CustomNamingFormat", CustomNamingFormat);

            foreach (KeyValuePair<int, List<ShowRule>> kvp in SeasonRules)
            {
                if (kvp.Value.Count > 0)
                {
                    writer.WriteStartElement("Rules");
                    writer.WriteAttributeToXml("SeasonNumber", kvp.Key);

                    foreach (ShowRule r in kvp.Value)
                    {
                        r.WriteXml(writer);
                    }

                    writer.WriteEndElement(); // Rules
                }
            }
            foreach (KeyValuePair<int, List<string>> kvp in ManualFolderLocations)
            {
                if (kvp.Value.Count > 0)
                {
                    writer.WriteStartElement("SeasonFolders");

                    writer.WriteAttributeToXml("SeasonNumber", kvp.Key);

                    foreach (string s in kvp.Value)
                    {
                        writer.WriteStartElement("Folder");
                        writer.WriteAttributeToXml("Location", s);
                        writer.WriteEndElement(); // Folder
                    }

                    writer.WriteEndElement(); // Rules
                }
            }
            writer.WriteEndElement(); // ShowItem
        }

        // ReSharper disable once UnusedMember.Global
        [NotNull]
        public Dictionary<int, List<ProcessedEpisode>> GetDvdSeasons()
        {
            //We will create this on the fly
            Dictionary<int, List<ProcessedEpisode>> returnValue = new Dictionary<int, List<ProcessedEpisode>>();
            foreach (KeyValuePair<int, List<ProcessedEpisode>> kvp in SeasonEpisodes)
            {
                foreach (ProcessedEpisode ep in kvp.Value)
                {
                    if (!returnValue.ContainsKey(ep.DvdSeasonNumber))
                    {
                        returnValue.Add(ep.DvdSeasonNumber, new List<ProcessedEpisode>());
                    }
                    returnValue[ep.DvdSeasonNumber].Add(ep);
                }
            }

            return returnValue;
        }

        protected override Dictionary<int, SafeList<string>> AllFolderLocations(bool manualToo, bool checkExist)
        {
            Dictionary<int, SafeList<string>> fld = new Dictionary<int, SafeList<string>>();

            if (manualToo)
            {
                foreach (KeyValuePair<int, List<string>> kvp in ManualFolderLocations.ToList())
                {
                    if (!fld.ContainsKey(kvp.Key))
                    {
                        fld[kvp.Key] = new SafeList<string>();
                    }

                    foreach (string s in kvp.Value)
                    {
                        fld[kvp.Key].Add(s.TrimSlash());
                    }
                }
            }

            if (AutoAddNewSeasons() && !string.IsNullOrEmpty(AutoAddFolderBase))
            {
                foreach (int i in ActiveSeasons.Keys())
                {
                    if (ManualFoldersReplaceAutomatic && fld.ContainsKey(i))
                    {
                        continue;
                    }

                    string newName = AutoFolderNameForSeason(i);
                    if (string.IsNullOrEmpty(newName))
                    {
                        continue;
                    }

                    if (checkExist && !Directory.Exists(newName))
                    {
                        continue;
                    }

                    //Now we can add the automated one
                    if (!fld.ContainsKey(i))
                    {
                        fld[i] = new SafeList<string>();
                    }

                    if (!fld[i].Contains(newName))
                    {
                        fld[i].Add(newName.TrimSlash());
                    }
                }
            }
            return fld;
        }

        public ProcessedSeason? GetSeason(int snum)
        {
            ConcurrentDictionary<int, ProcessedSeason> ssn = AppropriateSeasons();
            return ssn.ContainsKey(snum) ? ssn[snum] : null;
        }

        public void AddSeasonRule(int snum, ShowRule sr)
        {
            if (!SeasonRules.ContainsKey(snum))
            {
                SeasonRules[snum] = new List<ShowRule>();
            }

            SeasonRules[snum].Add(sr);
        }

        public ConcurrentDictionary<int, ProcessedSeason> AppropriateSeasons() => DvdOrder ? dvdSeasons : airedSeasons;

        public ProcessedSeason? GetFirstAvailableSeason()
        {
            return AppropriateSeasons().Select(x => x.Value).FirstOrDefault();
        }

        public ProcessedEpisode? GetFirstAvailableEpisode()
        {
            return SeasonEpisodes.Values.SelectMany(season => season).FirstOrDefault();
        }

        public bool InOneFolder() => AutoAddType == AutomaticFolderType.baseOnly;

        [NotNull]
        public string AutoFolderNameForSeason(int snum) => AutoFolderNameForSeason(GetSeason(snum));

        public bool AutoAddNewSeasons() => AutoAddType != AutomaticFolderType.none;

        public bool NoAirdatesUntilNow(int maxSeasonNumber)
        {
            int lastPossibleSeason = SeasonEpisodes.Keys.DefaultIfEmpty(0).Max();

            // for specials "season", see if any season has any aired dates
            // otherwise, check only up to the season we are considering
            int maxSeasonToUse = maxSeasonNumber <= 0 ? lastPossibleSeason : maxSeasonNumber;

            foreach (int snum in Enumerable.Range(1, maxSeasonToUse))
            {
                if (HasAnyAirdates(snum))
                {
                    return false;
                }

                //If the show is in its first season and no episodes have air dates
                if (lastPossibleSeason == 1)
                {
                    return false;
                }
            }

            return true;
        }

        [NotNull]
        public IEnumerable<int> GetSeasonKeys()
        {
            int[] numbers = new int[SeasonEpisodes.Keys.Count];
            SeasonEpisodes.Keys.CopyTo(numbers, 0);
            return numbers;
        }

        public void ClearEpisodes()
        {
            SeasonEpisodes.Clear();
            airedSeasons.Clear();
            dvdSeasons.Clear();
        }

        [NotNull]
        public IEnumerable<Episode> EpisodesToUse()
        {
            List<Episode> returnValue = new List<Episode>();
            ConcurrentDictionary<int, ProcessedSeason> seasonsToUse = AppropriateSeasons();

            foreach (KeyValuePair<int, ProcessedSeason> kvp in seasonsToUse)
            {
                if (kvp.Value?.Episodes.Values is null)
                {
                    continue;
                }

                if (IgnoreSeasons.Contains(kvp.Value.SeasonNumber))
                {
                    continue;
                }

                if (kvp.Value.SeasonNumber == 0 && TVSettings.Instance.IgnoreAllSpecials)
                {
                    continue;
                }

                returnValue.AddRange(kvp.Value.Episodes.Values);
            }

            return returnValue;
        }

        public CachedSeriesInfo? CachedShow => (CachedSeriesInfo)CachedData;
    }
}
