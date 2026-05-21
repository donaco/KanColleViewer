using System.Windows;
using System.Windows.Controls;

namespace Grabacr07.KanColleViewer.Controls.Metro
{
    /// <summary>
    /// タブナビゲーション用のリストコントロールです。
    /// Phase 4: MetroRadiance.UI.Controls.TabView の完全新実装です。
    /// </summary>
    public class TabView : ListBox
    {
        static TabView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(TabView),
                new FrameworkPropertyMetadata(typeof(TabView)));
        }

        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            base.OnSelectionChanged(e);

            foreach (var item in e.RemovedItems)
            {
                if (item is ITabItem removed) removed.IsSelected = false;
            }
            foreach (var item in e.AddedItems)
            {
                if (item is ITabItem added) added.IsSelected = true;
            }
        }
    }

    /// <summary>
    /// TabView のタブアイテムが実装するインターフェイスです。
    /// </summary>
    public interface ITabItem
    {
        bool IsSelected { get; set; }
    }
}
