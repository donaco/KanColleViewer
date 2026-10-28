using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Grabacr07.KanColleViewer.Models.Settings;

namespace Grabacr07.KanColleViewer.Views.Contents
{
	/// <summary>
	/// Overview.xaml の相互作用ロジック
	/// </summary>
	public partial class Overview
	{
		public Overview()
		{
			InitializeComponent();
		}

		/// <summary>
		/// ウィンドウランチャーを開いたとき、項目の表示可否を更新します。
		/// </summary>
		private void LauncherMenu_Checked(object sender, RoutedEventArgs e)
		{
			if (!LauncherSettings.ShowWindowLauncher.Value)
			{
				this.LauncherMenu.IsChecked = false;
				return;
			}

			(this.DataContext as ViewModels.Contents.OverviewViewModel)?.Launcher.RefreshAvailability();
		}
	}
}
