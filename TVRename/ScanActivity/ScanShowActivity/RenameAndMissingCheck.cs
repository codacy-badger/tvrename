using Alphaleonis.Win32.Filesystem;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TVRename
{
    internal class RenameAndMissingCheck : ScanShowActivity
    {
        private readonly DownloadIdentifiersController downloadIdentifiers;

        [NotNull]
        protected override string ActivityName() => "Rename & Missing Check";

        public RenameAndMissingCheck([NotNull] TVDoc doc) : base(doc)
        {
            downloadIdentifiers = new DownloadIdentifiersController();
        }

        protected override void Check([NotNull] ShowConfiguration si, DirFilesCache dfc, TVDoc.ScanSettings settings)
        {
            Dictionary<int, SafeList<string>> allFolders = si.AllExistngFolderLocations();
            if (allFolders.Count == 0) // no folders defined for this show
            {
                return; // so, nothing to do.
            }

            //This is the code that will iterate over the DownloadIdentifiers and ask each to ensure that
            //it has all the required files for that show
            if (!string.IsNullOrEmpty(si.AutoAddFolderBase) && allFolders.Any())
            {
                Doc.TheActionList.Add(downloadIdentifiers.ProcessShow(si));
            }

            //MS_TODO Put the banner refresh period into the settings file, we'll default to 3 months
            DateTime cutOff = DateTime.Now.AddMonths(-3);
            DateTime lastUpdate = si.BannersLastUpdatedOnDisk ?? DateTime.Now.AddMonths(-4);
            bool timeForBannerUpdate = cutOff.CompareTo(lastUpdate) == 1;

            if (TVSettings.Instance.NeedToDownloadBannerFile() && timeForBannerUpdate)
            {
                Doc.TheActionList.Add(
                    downloadIdentifiers.ForceUpdateShow(DownloadIdentifier.DownloadType.downloadImage, si));

                si.BannersLastUpdatedOnDisk = DateTime.Now;
                Doc.SetDirty();
            }

            // process each folder for each season...

            foreach (int snum in si.ActiveSeasons.Keys())
            {
                if (settings.Token.IsCancellationRequested)
                {
                    return;
                }

                if (!allFolders.ContainsKey(snum))
                {
                    continue;
                }

                // all the folders for this particular season
                SafeList<string> folders = allFolders[snum];

                CheckSeason(si, dfc, settings, snum, folders, timeForBannerUpdate);
            } // for each season of this show
        }

        private void CheckSeason([NotNull] ShowConfiguration si, DirFilesCache dfc, TVDoc.ScanSettings settings, int snum, [NotNull] SafeList<string> folders, bool timeForBannerUpdate)
        {
            bool folderNotDefined = folders.Count == 0;
            if (folderNotDefined && TVSettings.Instance.MissingCheck && !si.AutoAddNewSeasons())
            {
                return;
            }

            // base folder:
            if (!string.IsNullOrEmpty(si.AutoAddFolderBase) && si.AutoAddType != ShowConfiguration.AutomaticFolderType.none)
            {
                // main image for the folder itself
                Doc.TheActionList.Add(downloadIdentifiers.ProcessShow(si));
            }

            foreach (string folder in folders)
            {
                CheckSeasonFolder(si, dfc, settings, snum, timeForBannerUpdate, folder);
            } // for each folder for this season of this show
        }

        private void CheckSeasonFolder(ShowConfiguration si, DirFilesCache dfc, [NotNull] TVDoc.ScanSettings settings, int snum,
            bool timeForBannerUpdate, string folder)
        {
            if (settings.Token.IsCancellationRequested)
            {
                return;
            }

            if (TVSettings.Instance.NeedToDownloadBannerFile() && timeForBannerUpdate)
            {
                //Image cachedSeries checks here
                Doc.TheActionList.Add(
                    downloadIdentifiers.ForceUpdateSeason(DownloadIdentifier.DownloadType.downloadImage, si,
                        folder, snum));
            }

            //Image cachedSeries checks here
            Doc.TheActionList.Add(downloadIdentifiers.ProcessSeason(si, folder, snum));

            FileInfo[] files = dfc.GetFiles(folder);

            bool renCheck =
                TVSettings.Instance.RenameCheck && si.DoRename &&
                Directory.Exists(folder); // renaming check needs the folder to exist

            bool missCheck = TVSettings.Instance.MissingCheck && si.DoMissingCheck;

            if (!renCheck && !missCheck)
            {
                return;
            }

            Dictionary<int, FileInfo> localEps = new();
            int maxEpNumFound = 0;

            if (!si.SeasonEpisodes.ContainsKey(snum))
            {
                return;
            }

            List<ProcessedEpisode> eps = si.SeasonEpisodes[snum];

            foreach (FileInfo fi in files)
            {
                if (settings.Token.IsCancellationRequested)
                {
                    return;
                }

                if (!FinderHelper.FindSeasEp(fi, out int seasNum, out int epNum, out int _, si,
                    out TVSettings.FilenameProcessorRE _))
                {
                    continue; // can't find season & episode, so this file is of no interest to us
                }

                if (seasNum == -1)
                {
                    seasNum = snum;
                }

                ProcessedEpisode ep = eps.Find(x => x.AppropriateEpNum == epNum && x.AppropriateSeasonNumber == seasNum);

                if (ep is null)
                {
                    continue; // season+episode number don't correspond to any episode we know of
                }

                FileInfo actualFile = fi;

                // == RENAMING CHECK ==
                if (renCheck && TVSettings.Instance.FileHasUsefulExtension(fi, true))
                {
                    // Note that the extension of the file may not be fi.extension as users can put ".mkv.t" for example as an extension
                    string otherExtension = TVSettings.Instance.FileHasUsefulExtensionDetails(fi, true);

                    string newName = TVSettings.Instance.FilenameFriendly(
                        TVSettings.Instance.NamingStyle.NameFor(ep, otherExtension, folder.Length));

                    FileInfo fileWorthAdding = CheckFile(folder, fi, actualFile, newName, ep, files);

                    if (fileWorthAdding != null)
                    {
                        localEps[epNum] = fileWorthAdding;
                    }
                }

                // == MISSING CHECK part 1/2 ==
                if (missCheck && fi.IsMovieFile())
                {
                    // first pass of missing check is to tally up the episodes we do have
                    if (!localEps.ContainsKey(epNum))
                    {
                        localEps[epNum] = actualFile;
                    }

                    if (epNum > maxEpNumFound)
                    {
                        maxEpNumFound = epNum;
                    }
                }
            } // foreach file in folder

            // == MISSING CHECK part 2/2 (includes NFO and Thumbnails) ==
            // look at the official list of episodes, and look to see if we have any gaps

            DateTime today = DateTime.Now;
            foreach (ProcessedEpisode episode in eps)
            {
                if (!localEps.ContainsKey(episode.AppropriateEpNum)) // not here locally
                {
                    AddMissingIfNeeded(si, snum, folder, missCheck, episode, today);
                }
                else
                {
                    if (settings.Type == TVSettings.ScanType.Full)
                    {
                        Doc.CurrentStats.NsNumberOfEpisodes++;
                    }

                    // do NFO and thumbnail checks if required
                    FileInfo filo = localEps[episode.AppropriateEpNum]; // filename (or future filename) of the file
                    Doc.TheActionList.Add(downloadIdentifiers.ProcessEpisode(episode, filo));
                }
            } // up to date check, for each episode
        }

        private void AddMissingIfNeeded(ShowConfiguration si, int snum, string folder, bool missCheck, ProcessedEpisode episode,
            DateTime today)
        {
            // second part of missing check is to see what is missing!
            if (missCheck)
            {
                DateTime? dt = episode.GetAirDateDt(true);
                bool dtOk = dt != null;

                bool notFuture =
                    dtOk && dt.Value.CompareTo(today) < 0; // isn't an episode yet to be aired

                // only add to the missing list if, either:
                // - force check is on
                // - there are no aired dates at all, for up to and including this season
                // - there is an aired date, and it isn't in the future
                bool noAirdatesUntilNow = si.NoAirdatesUntilNow(snum);
                bool siForceCheckFuture = (si.ForceCheckFuture || notFuture) && dtOk;
                bool siForceCheckNoAirdate = si.ForceCheckNoAirdate && !dtOk;

                if (noAirdatesUntilNow || siForceCheckFuture || siForceCheckNoAirdate)
                {
                    // then add it as officially missing
                    Doc.TheActionList.Add(new ShowItemMissing(episode, folder));
                }
            } // if doing missing check
        }

        private FileInfo? CheckFile([NotNull] string folder, FileInfo fi, [NotNull] FileInfo actualFile, string newName, ProcessedEpisode ep, IEnumerable<FileInfo> files)
        {
            if (TVSettings.Instance.RetainLanguageSpecificSubtitles)
            {
                (bool isSubtitleFile, string subtitleExtension) = fi.IsLanguageSpecificSubtitle();

                if (isSubtitleFile && actualFile.Name != newName)
                {
                    newName = TVSettings.Instance.FilenameFriendly(
                        TVSettings.Instance.NamingStyle.NameFor(ep, subtitleExtension, folder.Length));
                }
            }

            FileInfo newFile = FileHelper.FileInFolder(folder, newName); // rename updates the filename

            if (!string.Equals(newFile.FullName, actualFile.FullName, TVSettings.Instance.FileNameComparisonType))
            {
                //Check that the file does not already exist
                //if (FileHelper.FileExistsCaseSensitive(newFile.FullName))
                if (FileHelper.FileExistsCaseSensitive(files, newFile))
                {
                    LOGGER.Warn(
                        $"Identified that {actualFile.FullName} should be renamed to {newName}, but it already exists.");
                }
                else
                {
                    LOGGER.Info($"Identified that {actualFile.FullName} should be renamed to {newName}.");
                    Doc.TheActionList.Add(new ActionCopyMoveRename(ActionCopyMoveRename.Op.rename, fi,
                        newFile, ep, false, null, Doc));

                    //The following section informs the DownloadIdentifers that we already plan to
                    //copy a file in the appropriate place and they do not need to worry about downloading
                    //one for that purpose
                    downloadIdentifiers.NotifyComplete(newFile);

                    if (newFile.IsMovieFile())
                    {
                        return newFile;
                    }
                }
            }
            else
            {
                if (actualFile.IsMovieFile())
                {
                    //File is correct name
                    LOGGER.Debug($"Identified that {actualFile.FullName} is in the right place. Marking it as 'seen'.");
                    //Record this episode as seen

                    TVSettings.Instance.PreviouslySeenEpisodes.EnsureAdded(ep);
                    if (TVSettings.Instance.IgnorePreviouslySeen)
                    {
                        Doc.SetDirty();
                    }
                }
            }

            return null;
        }

        protected override bool Active() => true;
    }
}
