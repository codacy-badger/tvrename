//
// Main website for TVRename is http://tvrename.com
//
// Source code available at https://github.com/TV-Rename/tvrename
//
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
//

using Alphaleonis.Win32.Filesystem;
using JetBrains.Annotations;

namespace TVRename
{
    public abstract class ActionWriteMetadata : ActionDownload
    {
        protected readonly FileInfo Where;
        protected readonly ShowConfiguration? SelectedShow; // if for an entire show, rather than specific episode

        protected ActionWriteMetadata(FileInfo where, ShowConfiguration sI)
        {
            Where = where;
            SelectedShow = sI;
        }

        protected ActionWriteMetadata(FileInfo where, MovieConfiguration mc)
        {
            Where = where;
            Movie = mc;
        }

        public override string Produces => Where.FullName;

        public override string ProgressText => Where.Name;

        public override long SizeOfWork => 10000;

        public override string TargetFolder => Where.DirectoryName;

        [NotNull]
        public override IgnoreItem Ignore => new(Where.FullName);

        [NotNull]
        public override string ScanListViewGroup => "lvgActionMeta";

        public override int IconNumber => 7;

        public override string SeriesName => Episode?.Show.ShowName ?? SelectedShow?.ShowName ?? Movie!.ShowName;
        public override string DestinationFolder => Where.DirectoryName;
        public override string DestinationFile => Where.Name;
    }
}
