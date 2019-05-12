// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 
using System;
using Alphaleonis.Win32.Filesystem;
using System.Xml;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using JetBrains.Annotations;

// These are what is used when processing folders for missing episodes, renaming, etc. of files.

// A "ProcessedEpisode" is generated by processing an Episode from thetvdb, and merging/renaming/etc.
//
// A "ShowItem" is a show the user has added on the "My Shows" tab

namespace TVRename
{
    public class ShowItem
    {
        public string AutoAddFolderBase;
        public string AutoAddCustomFolderFormat;
        public AutomaticFolderType AutoAddType;

        public bool CountSpecials;
        public bool DvdOrder; // sort by DVD order, not the default sort we get
        public bool DoMissingCheck;
        public bool DoRename;
        public bool ForceCheckFuture;
        public bool ForceCheckNoAirdate;
        public List<int> IgnoreSeasons;
        public Dictionary<int, List<string>> ManualFolderLocations;
        public Dictionary<int, List<ProcessedEpisode>> SeasonEpisodes; // built up by applying rules.
        public Dictionary<int, List<ShowRule>> SeasonRules;
        public bool ShowNextAirdate;
        public int TvdbCode;
        public bool UseCustomShowName;
        public string CustomShowName;
        public bool UseCustomLanguage;
        public string CustomLanguageCode;
        public bool UseSequentialMatch;
        public readonly List<string> AliasNames = new List<string>();
        public bool UseCustomSearchUrl;
        public string CustomSearchUrl;
        public bool ManualFoldersReplaceAutomatic;

        public string ShowTimeZone;
        private TimeZoneInfo seriesTimeZone;
        private string lastFiguredTz;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public DateTime? BannersLastUpdatedOnDisk { get; set; }

        public Season.SeasonType Order => DvdOrder ? Season.SeasonType.dvd : Season.SeasonType.aired;

        #region AutomaticFolderType enum
        public enum AutomaticFolderType
        {
            none,
            baseOnly,
            libraryDefault,
            custom
        }
        #endregion

        public ShowItem()
        {
            SetDefaults();
        }

        public ShowItem(int tvdbCode)
        {
            SetDefaults();
            TvdbCode = tvdbCode;
        }

        private void FigureOutTimeZone()
        {
            string tzstr = ShowTimeZone;

            if (string.IsNullOrEmpty(tzstr))
            {
                tzstr = TimeZoneHelper.DefaultTimeZone();
            }

            try
            {
                seriesTimeZone = TimeZoneInfo.FindSystemTimeZoneById(tzstr);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Could not work out what timezone '{ShowName}' has. In the settings it uses '{tzstr}', but that is not valid. Please update. Using the default timezone {TimeZoneHelper.DefaultTimeZone()} for the show instead.");
                try
                {
                    tzstr = TimeZoneHelper.DefaultTimeZone();
                    seriesTimeZone = TimeZoneInfo.FindSystemTimeZoneById(tzstr);
                }
                catch (Exception)
                {
                    Logger.Warn(ex, $"Could not work out what timezone '{ShowName}' has. In the settings it uses '{tzstr}', but that is not valid. Tried to use the default timezone {TimeZoneHelper.DefaultTimeZone()} for the show instead - also invalid.  Please update.");
                    seriesTimeZone = TimeZoneInfo.Local;
                }
            }

            lastFiguredTz = tzstr;
        }

        public TimeZoneInfo GetTimeZone()
        {
            if (seriesTimeZone is null || lastFiguredTz != ShowTimeZone)
            {
                FigureOutTimeZone();
            }
            return seriesTimeZone;
        }

        public ShowItem([NotNull] XElement xmlSettings)
        {
            SetDefaults();

            CustomShowName = xmlSettings.ExtractString("ShowName");
            UseCustomShowName = xmlSettings.ExtractBool("UseCustomShowName",false);
            UseCustomLanguage = xmlSettings.ExtractBool("UseCustomLanguage",false);
            CustomLanguageCode = xmlSettings.ExtractString("CustomLanguageCode");
            CustomShowName = xmlSettings.ExtractString("CustomShowName");
            TvdbCode = xmlSettings.ExtractInt("TVDBID",-1);
            CountSpecials = xmlSettings.ExtractBool("CountSpecials",false);
            ShowNextAirdate = xmlSettings.ExtractBool("ShowNextAirdate",true);
            AutoAddFolderBase = xmlSettings.ExtractString("FolderBase");
            DoRename = xmlSettings.ExtractBool("DoRename",true);
            DoMissingCheck = xmlSettings.ExtractBool("DoMissingCheck",true);
            DvdOrder = xmlSettings.ExtractBool("DVDOrder",false);
            UseCustomSearchUrl = xmlSettings.ExtractBool("UseCustomSearchURL",false);
            CustomSearchUrl = xmlSettings.ExtractString("CustomSearchURL");
            ShowTimeZone = xmlSettings.ExtractString("TimeZone") ?? TimeZoneHelper.DefaultTimeZone(); // default, is correct for most shows;
            ForceCheckFuture = xmlSettings.ExtractBoolBackupDefault("ForceCheckFuture","ForceCheckAll",false);
            ForceCheckNoAirdate = xmlSettings.ExtractBoolBackupDefault("ForceCheckNoAirdate","ForceCheckAll",false);
            AutoAddCustomFolderFormat = xmlSettings.ExtractString("CustomFolderFormat") ?? CustomSeasonName.DefaultStyle();
            AutoAddType = GetAutoAddType(xmlSettings.ExtractInt("AutoAddType"));
            BannersLastUpdatedOnDisk = xmlSettings.ExtractDateTime("BannersLastUpdatedOnDisk");
            UseSequentialMatch = xmlSettings.ExtractBool("UseSequentialMatch",false);
            ManualFoldersReplaceAutomatic = xmlSettings.ExtractBool("ManualFoldersReplaceAutomatic", false);

            SetupIgnoreRules(xmlSettings);
            SetupAliases(xmlSettings);
            SetupSeasonRules(xmlSettings);
            SetupSeasonFolders(xmlSettings);
            UpgradeFromOldSeasonFormat(xmlSettings);
        }

        private static AutomaticFolderType GetAutoAddType(int? value)
        {
            return value is null? AutomaticFolderType.libraryDefault: (AutomaticFolderType)value;
        }

        private void UpgradeFromOldSeasonFormat([NotNull] XElement xmlSettings)
        {
            //These variables have been discontinued (JULY 2018).  If we have any then we should migrate to the new values
            bool upgradeFromOldAutoAddFunction = xmlSettings.Descendants("AutoAddNewSeasons").Any()
                                                 || xmlSettings.Descendants("FolderPerSeason").Any()
                                                 || xmlSettings.Descendants("SeasonFolderName").Any()
                                                 || xmlSettings.Descendants("PadSeasonToTwoDigits").Any();
            bool tempAutoAddNewSeasons = xmlSettings.ExtractBool("AutoAddNewSeasons",true);
            bool tempAutoAddFolderPerSeason = xmlSettings.ExtractBool("FolderPerSeason",true);
            string tempAutoAddSeasonFolderName = xmlSettings.ExtractString("SeasonFolderName");
            bool tempPadSeasonToTwoDigits = xmlSettings.ExtractBool("PadSeasonToTwoDigits",true);

            if (upgradeFromOldAutoAddFunction)
            {
                if (tempAutoAddNewSeasons)
                {
                    if (tempAutoAddFolderPerSeason)
                    {
                        AutoAddCustomFolderFormat = tempAutoAddSeasonFolderName + ((tempPadSeasonToTwoDigits || TVSettings.Instance.LeadingZeroOnSeason) ? "{Season:2}" : "{Season}");
                        AutoAddType = (AutoAddCustomFolderFormat == TVSettings.Instance.SeasonFolderFormat)
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

        private void SetupAliases([NotNull] XElement xmlSettings)
        {
            foreach (string alias in xmlSettings.Descendants("AliasNames").Descendants("Alias").Select(alias=> alias.Value).Distinct())
            {
                AliasNames.Add(alias);
            }
        }

        private void SetupSeasonRules([NotNull] XElement xmlSettings)
        {
            foreach (XElement rulesSet in xmlSettings.Descendants("Rules"))
            {
                XAttribute value = rulesSet.Attribute("SeasonNumber");
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
                XAttribute value = seasonFolder.Attribute("SeasonNumber");
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

        [CanBeNull]
        public SeriesInfo TheSeries() => TheTVDB.Instance.GetSeries(TvdbCode);

        public string ShowName
        {
            get
            {
                if (UseCustomShowName)
                {
                    return CustomShowName;
                }

                SeriesInfo ser = TheSeries();
                if (ser != null)
                {
                    return ser.Name;
                }

                return "<" + TvdbCode + " not downloaded>";
            }
        }

        [NotNull]
        private IEnumerable<string> GetSimplifiedPossibleShowNames()
        {
            List<string> possibles = new List<string>();

            string simplifiedShowName = Helpers.SimplifyName(ShowName);
            if (simplifiedShowName != "") { possibles.Add( simplifiedShowName); }

            //Check the custom show name too
            if (UseCustomShowName)
            {
                string simplifiedCustomShowName = Helpers.SimplifyName(CustomShowName);
                if (simplifiedCustomShowName != "") { possibles.Add(simplifiedCustomShowName); }
            }

            //Also add the aliases provided
            possibles.AddNullableRange(AliasNames.Select(Helpers.SimplifyName));

            //Also use the aliases from theTVDB
            possibles.AddNullableRange(TheSeries()?.Aliases()?.Select(Helpers.SimplifyName));

            return possibles;
        }

        public bool NameMatch([NotNull] FileSystemInfo file,bool useFullPath) => NameMatch(useFullPath ? file.FullName: file.Name);
        
        public bool NameMatch(string text)
        {
            return GetSimplifiedPossibleShowNames().Any(name => FileHelper.SimplifyAndCheckFilename(text, name));
        }
        public bool NameMatchInitial(string text)
        {
            return GetSimplifiedPossibleShowNames().Any(name => FileHelper.SimplifyAndCheckFilename(text, name));
        }

        public bool NameMatchFilters(string text)
        {
                return GetSimplifiedPossibleShowNames().Any(name => name.Contains(Helpers.SimplifyName(text), StringComparison.OrdinalIgnoreCase));
        }

        public string ShowStatus
        {
            get{
                SeriesInfo ser = TheSeries();
                if (ser != null )
                {
                    return ser.Status;
                }

                return "Unknown";
            }
        }

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
                if (HasSeasonsAndEpisodes)
                {
                    if (HasAiredEpisodes && !HasUnairedEpisodes)
                    {
                        return ShowAirStatus.aired;
                    }
                    else if (HasUnairedEpisodes && !HasAiredEpisodes)
                    {
                        return ShowAirStatus.noneAired;
                    }
                    else if (HasAiredEpisodes && HasUnairedEpisodes)
                    {
                        return ShowAirStatus.partiallyAired;
                    }
                    else
                    {
                        //System.Diagnostics.Debug.Assert(false, "That is weird ... we have 'seasons and episodes' but none are aired, nor unaired. That case shouldn't actually occur !");
                        return ShowAirStatus.noEpisodesOrSeasons;
                    }
                }
                else
                {
                    return ShowAirStatus.noEpisodesOrSeasons;
                }
            }
        }

        private bool HasSeasonsAndEpisodes
        {
            get {
                //We can use AiredSeasons as it does not matter which order we do this in Aired or DVD
                SeriesInfo seriesInfo = TheSeries();
                if (seriesInfo?.AiredSeasons is null || seriesInfo.AiredSeasons.Count <= 0)
                {
                    return false;
                }

                foreach (KeyValuePair<int, Season> s in seriesInfo.AiredSeasons)
                {
                    if(IgnoreSeasons.Contains(s.Key))
                    {
                        continue;
                    }

                    if (TVSettings.Instance.IgnoreAllSpecials && s.Key == 0)
                    {
                        continue;
                    }

                    if (s.Value.Episodes != null && s.Value.Episodes.Count > 0)
                    {
                        return true;
                    }
                }
                return false;
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

                SeriesInfo seriesInfo = TheSeries();
                if (seriesInfo is null)
                {
                    return true;
                }

                foreach (KeyValuePair<int, Season> s in seriesInfo.AiredSeasons)
                {
                    if (IgnoreSeasons.Contains(s.Key))
                    {
                        continue;
                    }

                    if (TVSettings.Instance.IgnoreAllSpecials && s.Key == 0)
                    {
                        continue;
                    }

                    if (s.Value.Status(GetTimeZone()) == Season.SeasonStatus.noneAired ||
                        s.Value.Status(GetTimeZone()) == Season.SeasonStatus.partiallyAired)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private bool HasAiredEpisodes
        {
                get{
                    if (!HasSeasonsAndEpisodes)
                    {
                        return false;
                    }

                    SeriesInfo seriesInfo = TheSeries();
                    if (seriesInfo is null)
                    {
                        return false;
                    }

                foreach (KeyValuePair<int, Season> s in seriesInfo.AiredSeasons)
                    {
                        if(IgnoreSeasons.Contains(s.Key))
                        {
                            continue;
                        }

                        if (TVSettings.Instance.IgnoreAllSpecials && s.Key == 0)
                        {
                            continue;
                        }

                        if (s.Value.Status(GetTimeZone()) == Season.SeasonStatus.partiallyAired || s.Value.Status(GetTimeZone()) == Season.SeasonStatus.aired)
                        {
                            return true;
                        }
                    }
                    return false;
             }
        }
        
        [NotNull]
        public IEnumerable<string> Genres => TheSeries()?.Genres()??new List<string>();

        [NotNull]
        public IEnumerable<Actor> Actors => TheSeries()?.GetActors() ?? new List<Actor>();

        [CanBeNull]
        public Language  PreferredLanguage => UseCustomLanguage ? TheTVDB.Instance.LanguageList.GetLanguageFromCode(CustomLanguageCode) : TheTVDB.Instance.PreferredLanguage;

        private void SetDefaults()
        {
            ManualFolderLocations = new Dictionary<int, List<string>>();
            IgnoreSeasons = new List<int>();
            UseCustomShowName = false;
            CustomShowName = "";
            UseCustomLanguage = false;
            UseSequentialMatch = false;
            SeasonRules = new Dictionary<int, List<ShowRule>>();
            SeasonEpisodes = new Dictionary<int, List<ProcessedEpisode>>();
            ShowNextAirdate = true;
            TvdbCode = -1;
            AutoAddFolderBase = "";
            AutoAddCustomFolderFormat = CustomSeasonName.DefaultStyle();
            AutoAddType = AutomaticFolderType.libraryDefault;
            DoRename = true;
            DoMissingCheck = true;
            CountSpecials = false;
            DvdOrder = false;
            CustomSearchUrl = "";
            UseCustomSearchUrl = false;
            ForceCheckNoAirdate = false;
            ForceCheckFuture = false;
            ManualFoldersReplaceAutomatic = false;
            BannersLastUpdatedOnDisk = null; //assume that the banners are old and have expired
            ShowTimeZone = TimeZoneHelper.DefaultTimeZone(); // default, is correct for most shows
            lastFiguredTz = "";
        }

        [CanBeNull]
        public List<ShowRule> RulesForSeason(int n)
        {
            return SeasonRules.ContainsKey(n) ? SeasonRules[n] : null;
        }

        [NotNull]
        private string AutoFolderNameForSeason(Season s)
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

            if (s.IsSpecial())
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

            XmlHelper.WriteElementToXml(writer,"UseCustomShowName",UseCustomShowName);
            XmlHelper.WriteElementToXml(writer,"CustomShowName",CustomShowName);
            XmlHelper.WriteElementToXml(writer, "UseCustomLanguage", UseCustomLanguage);
            XmlHelper.WriteElementToXml(writer, "CustomLanguageCode", CustomLanguageCode);
            XmlHelper.WriteElementToXml(writer,"ShowNextAirdate",ShowNextAirdate);
            XmlHelper.WriteElementToXml(writer,"TVDBID",TvdbCode);
            XmlHelper.WriteElementToXml(writer, "FolderBase", AutoAddFolderBase);
            XmlHelper.WriteElementToXml(writer,"DoRename",DoRename);
            XmlHelper.WriteElementToXml(writer,"DoMissingCheck",DoMissingCheck);
            XmlHelper.WriteElementToXml(writer,"CountSpecials",CountSpecials);
            XmlHelper.WriteElementToXml(writer,"DVDOrder",DvdOrder);
            XmlHelper.WriteElementToXml(writer,"ForceCheckNoAirdate",ForceCheckNoAirdate);
            XmlHelper.WriteElementToXml(writer,"ForceCheckFuture",ForceCheckFuture);
            XmlHelper.WriteElementToXml(writer,"UseSequentialMatch",UseSequentialMatch);
            XmlHelper.WriteElementToXml(writer, "CustomFolderFormat", AutoAddCustomFolderFormat);
            XmlHelper.WriteElementToXml(writer, "AutoAddType", (int)AutoAddType );
            XmlHelper.WriteElementToXml(writer, "BannersLastUpdatedOnDisk", BannersLastUpdatedOnDisk);
            XmlHelper.WriteElementToXml(writer, "TimeZone", ShowTimeZone);
            XmlHelper.WriteElementToXml(writer, "ManualFoldersReplaceAutomatic",ManualFoldersReplaceAutomatic);

            writer.WriteStartElement("IgnoreSeasons");
            foreach (int i in IgnoreSeasons)
            {
                XmlHelper.WriteElementToXml(writer,"Ignore",i);
            }
            writer.WriteEndElement();

            writer.WriteStartElement("AliasNames");
            foreach (string str in AliasNames)
            {
                XmlHelper.WriteElementToXml(writer,"Alias",str);
            }
            writer.WriteEndElement();

            XmlHelper.WriteElementToXml(writer, "UseCustomSearchURL", UseCustomSearchUrl);
            XmlHelper.WriteElementToXml(writer, "CustomSearchURL",CustomSearchUrl);

            foreach (KeyValuePair<int, List<ShowRule>> kvp in SeasonRules)
            {
                if (kvp.Value.Count > 0)
                {
                    writer.WriteStartElement("Rules");
                    XmlHelper.WriteAttributeToXml(writer ,"SeasonNumber",kvp.Key);

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

                    XmlHelper.WriteAttributeToXml(writer,"SeasonNumber",kvp.Key);

                    foreach (string s in kvp.Value)
                    {
                        writer.WriteStartElement("Folder");
                        XmlHelper.WriteAttributeToXml(writer,"Location",s);
                        writer.WriteEndElement(); // Folder
                    }

                    writer.WriteEndElement(); // Rules
                }
            }
            writer.WriteEndElement(); // ShowItem
        }

        [NotNull]
        public static List<ProcessedEpisode> ProcessedListFromEpisodes([NotNull] IEnumerable<Episode> el, ShowItem si)
        {
            List<ProcessedEpisode> pel = new List<ProcessedEpisode>();
            foreach (Episode e in el)
            {
                pel.Add(new ProcessedEpisode(e, si));
            }

            return pel;
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
                    if (!returnValue.ContainsKey(ep.DvdSeasonNumber ))
                    {
                        returnValue.Add(ep.DvdSeasonNumber, new List<ProcessedEpisode>());
                    }
                    returnValue[ep.DvdSeasonNumber].Add(ep);
                }
            }

            return returnValue;
        }

        [NotNull]
        public Dictionary<int, List<string>> AllExistngFolderLocations() => AllFolderLocations( true,true);
        [NotNull]
        public Dictionary<int, List<string>> AllProposedFolderLocations() => AllFolderLocations(true,false);

        [NotNull]
        public Dictionary<int, List<string>> AllFolderLocationsEpCheck(bool checkExist) => AllFolderLocations(true, checkExist);

        [NotNull]
        public Dictionary<int, List<string>> AllFolderLocations(bool manualToo)=> AllFolderLocations(manualToo,true);

        [NotNull]
        private Dictionary<int, List<string>> AllFolderLocations(bool manualToo,bool checkExist)
        {
            Dictionary<int, List<string>> fld = new Dictionary<int, List<string>>();

            if (manualToo)
            {
                foreach (KeyValuePair<int, List<string>> kvp in ManualFolderLocations.ToList())
                {
                    if (!fld.ContainsKey(kvp.Key))
                    {
                        fld[kvp.Key] = new List<string>();
                    }

                    foreach (string s in kvp.Value)
                    {
                        fld[kvp.Key].Add(s.TrimSlash());
                    }
                }
            }

            if (AutoAddNewSeasons() && (!string.IsNullOrEmpty(AutoAddFolderBase)))
            {
                foreach (int i in SeasonEpisodes.Keys.ToList())
                {
                    if (IgnoreSeasons.Contains(i))
                    {
                        continue;
                    }

                    if (i == 0 && TVSettings.Instance.IgnoreAllSpecials)
                    {
                        continue;
                    }

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
                        fld[i] = new List<string>();
                    }

                    if (!fld[i].Contains(newName))
                    {
                        fld[i].Add(newName.TrimSlash());
                    }
                }
            }
            return fld;
        }

        public static int CompareShowItemNames([NotNull] ShowItem one, [NotNull] ShowItem two)
        {
            string ones = one.ShowName; 
            string twos = two.ShowName; 
            return string.Compare(ones, twos, StringComparison.Ordinal);
        }

        [CanBeNull]
        public Season GetSeason(int snum)
        {
            Dictionary<int, Season> ssn = AppropriateSeasons();
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

        public Dictionary<int,Season> AppropriateSeasons()
        {
            SeriesInfo s = TheSeries();
            if (s==null)
            {
                return new Dictionary<int, Season>();
            }

            return DvdOrder ? s.DvdSeasons : s.AiredSeasons;
        }

        public Season GetFirstAvailableSeason()
        {
            foreach (KeyValuePair<int, Season> x in AppropriateSeasons())
            {
                return x.Value;
            }

            return null;
        }

        [CanBeNull]
        public ProcessedEpisode GetFirstAvailableEpisode()
        {
            foreach (List<ProcessedEpisode> season in SeasonEpisodes.Values)
            {
                foreach (ProcessedEpisode pe in season)
                {
                    if (!(pe is null))
                    {
                        return pe;
                    }
                }
            }

            return null;
        }

        public bool InOneFolder() => (AutoAddType == AutomaticFolderType.baseOnly);

        [NotNull]
        public string AutoFolderNameForSeason(int snum) => AutoFolderNameForSeason(GetSeason(snum));

        public bool AutoAddNewSeasons() => (AutoAddType != AutomaticFolderType.none);

        [NotNull]
        public IEnumerable<string> GetActorNames()
        {
            return Actors.Select(x => x.ActorName);
        }

        public bool NoAirdatesUntilNow(int snum)
        {
            int lastPossibleSeason = SeasonEpisodes.Keys.DefaultIfEmpty(0).Max();

            SeriesInfo ser = TheTVDB.Instance.GetSeries(TvdbCode);

            if (ser is null)
            {
                return true;
            }

            // for specials "season", see if any season has any aired dates
            // otherwise, check only up to the season we are considering
            int maxSeasonToUse = snum == 0 ? lastPossibleSeason : snum;

            foreach (int i in Enumerable.Range(1, maxSeasonToUse))
            {
                if (ser.HasAnyAirdates(i, Order))
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
    }
}
