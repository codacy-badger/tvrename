using System.Collections.Generic;
using System.Windows.Forms;

namespace TVRename
{
    public class PossibleDuplicateEpisode
    {
        private readonly ProcessedEpisode episodeOne;
        private readonly ProcessedEpisode episodeTwo;
        public readonly int SeasonNumber;
        public readonly bool AirDatesMatch;
        public readonly bool SimilarNames;
        public readonly bool OneFound;
        public readonly bool LargeFileSize;

        public PossibleDuplicateEpisode(ProcessedEpisode episodeOne, ProcessedEpisode episodeTwo, int season, bool airDatesMatch, bool similarNames, bool oneFound, bool largeFileSize)
        {
            this.episodeTwo = episodeTwo;
            this.episodeOne = episodeOne;
            this.SeasonNumber = season;
            this.AirDatesMatch = airDatesMatch;
            this.SimilarNames = similarNames;
            this.OneFound = oneFound;
            this.LargeFileSize = largeFileSize;
        }

        public ListViewItem PresentationView
        {
            get
            {
                ListViewItem lvi = new ListViewItem
                {
                    Text = episodeOne.SI.ShowName
                };

                lvi.SubItems.Add(episodeOne.AppropriateSeasonNumber.ToString());
                lvi.SubItems.Add(episodeOne.NumsAsString() + " & " + episodeTwo.NumsAsString());
                lvi.SubItems.Add(episodeOne.GetAirDateDT(true).PrettyPrint());
                lvi.SubItems.Add(episodeOne.Name + " & " + episodeTwo.Name);
                
                List<string> names = new List<string> {episodeOne.Name, episodeTwo.Name};
                string combinedName = ShowLibrary.GetBestNameFor(names, "");
                lvi.SubItems.Add(combinedName);

                lvi.Tag = this;

                return lvi;
            }
        }

        public ShowItem ShowItem => episodeTwo.SI;
        public ProcessedEpisode Episode => episodeOne;

        public ShowRule GenerateRule()
        {
            return new ShowRule
            {
                DoWhatNow = RuleAction.kMerge,
                First = episodeOne.AppropriateEpNum,
                Second = episodeTwo.AppropriateEpNum
            };
        }
    }
}
