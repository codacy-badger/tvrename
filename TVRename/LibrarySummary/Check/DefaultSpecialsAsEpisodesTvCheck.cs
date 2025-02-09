using JetBrains.Annotations;

namespace TVRename
{
    internal class DefaultSpecialsAsEpisodesTvCheck : DefaultTvShowCheck
    {
        public DefaultSpecialsAsEpisodesTvCheck([NotNull] ShowConfiguration show, TVDoc doc) : base(show, doc)
        {
        }

        [NotNull]
        protected override string FieldName => "Count Specials As Episodes Check";

        protected override bool Field => Show.CountSpecials;

        protected override bool Default => TVSettings.Instance.DefShowSpecialsCount;

        protected override void FixInternal()
        {
            Show.CountSpecials = Default;
        }
    }
}
