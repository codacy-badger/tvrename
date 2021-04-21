using JetBrains.Annotations;

namespace TVRename
{
    internal abstract class DefaultMovieCheck : MovieCheck
    {
        protected DefaultMovieCheck([NotNull] MovieConfiguration movie, TVDoc doc) : base(movie, doc)
        {
        }
        public override string CheckName => "[Movie] "+FieldName;
        protected abstract string FieldName { get; }
        protected abstract bool Field { get; }
        protected abstract bool Default { get; }

        public override bool Check() => Field != Default;

        public override string Explain() => $"Default value for '{FieldName}' is {Default}. For this Movie it is {Field}.";
    }
}
