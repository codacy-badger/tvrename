using JetBrains.Annotations;

namespace TVRename
{
    internal class DefaultFutureMovieCheck : DefaultMovieCheck
    {
        public DefaultFutureMovieCheck([NotNull] MovieConfiguration show, TVDoc doc) : base(show, doc)
        {
        }

        [NotNull]
        protected override string FieldName => "Do Future Movies Check";

        protected override bool Field => Movie.ForceCheckFuture;

        protected override bool Default => TVSettings.Instance.DefMovieCheckFutureDatedMovies;

        protected override void FixInternal()
        {
            Movie.ForceCheckFuture = Default;
        }
    }
}
