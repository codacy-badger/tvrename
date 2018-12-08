// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 
namespace TVRename
{
    using System.Windows.Forms;

    public class LvResults
    {
        #region WhichResults enum

        public enum WhichResults
        {
            Checked,
            Selected,
            All
        }

        #endregion

        public System.Collections.Generic.List<ActionCopyMoveRename> CopyMove;
        public int Count;
        public System.Collections.Generic.List<ActionDownloadImage> Download;
        public ItemList FlatList;
        public System.Collections.Generic.List<ItemMissing> Missing;
        public System.Collections.Generic.List<ActionNfo> NFO;
        public System.Collections.Generic.List<ActionPyTivoMeta> PyTivoMeta;
        public System.Collections.Generic.List<ActionTDownload> RSS;
        public System.Collections.Generic.List<ActionCopyMoveRename> Rename;

        public LvResults(ListView lv, bool isChecked) // if not checked, then selected items
        {
            Go(lv, isChecked ? WhichResults.Checked : WhichResults.Selected);
        }

        public LvResults(ListView lv, WhichResults which)
        {
            Go(lv, which);
        }

        private void Go(ListView lv, WhichResults which)
        {
            Missing = new System.Collections.Generic.List<ItemMissing>();
            RSS = new System.Collections.Generic.List<ActionTDownload>();
            CopyMove = new System.Collections.Generic.List<ActionCopyMoveRename>();
            Rename = new System.Collections.Generic.List<ActionCopyMoveRename>();
            Download = new System.Collections.Generic.List<ActionDownloadImage>();
            NFO = new System.Collections.Generic.List<ActionNfo>();
            PyTivoMeta = new System.Collections.Generic.List<ActionPyTivoMeta>();
            FlatList = new ItemList();

            System.Collections.Generic.List<ListViewItem> sel = GetSelections(lv, which);

            Count = sel.Count;

            if (sel.Count == 0)
                return;

            foreach (ListViewItem lvi in sel)
            {
                if (lvi == null)
                    continue;

                Item action = (Item)(lvi.Tag);
                if (action != null)
                    FlatList.Add(action);

                switch (action)
                {
                    case ActionCopyMoveRename cmr when cmr.Operation == ActionCopyMoveRename.Op.rename:
                        Rename.Add(cmr);
                        break;
                    // copy/move
                    case ActionCopyMoveRename cmr:
                        CopyMove.Add(cmr);
                        break;
                    case ActionDownloadImage item:
                        Download.Add(item);
                        break;
                    case ActionTDownload rss:
                        RSS.Add(rss);
                        break;
                    case ItemMissing missing:
                        Missing.Add(missing);
                        break;
                    case ActionNfo nfo:
                        NFO.Add(nfo);
                        break;
                    case ActionPyTivoMeta meta:
                        PyTivoMeta.Add(meta);
                        break;
                }
            }
        }

        private static System.Collections.Generic.List<ListViewItem> GetSelections(ListView lv, WhichResults which)
        {
            System.Collections.Generic.List<ListViewItem> sel = new System.Collections.Generic.List<ListViewItem>();
            switch (which)
            {
                case WhichResults.Checked:
                {
                    ListView.CheckedListViewItemCollection ss = lv.CheckedItems;
                    foreach (ListViewItem lvi in ss)
                        sel.Add(lvi);

                    break;
                }
                // all
                case WhichResults.Selected:
                {
                    ListView.SelectedListViewItemCollection ss = lv.SelectedItems;
                    foreach (ListViewItem lvi in ss)
                        sel.Add(lvi);

                    break;
                }
                default:
                    foreach (ListViewItem lvi in lv.Items)
                        sel.Add(lvi);

                    break;
            }

            return sel;
        }
    }
}
