using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
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
		/// 「ウィンドウランチャー」にマウスを合わせたとき、ウィンドウランチャーを表示します。
		/// </summary>
		private void LauncherMenu_MouseEnter(object sender, MouseEventArgs e)
		{
			if (e.LeftButton != MouseButtonState.Released
				|| e.MiddleButton != MouseButtonState.Released
				|| e.RightButton != MouseButtonState.Released)
			{
				return;
			}

			this.ShowLauncherPopup();
		}

		private void ShowLauncherPopup()
		{
			if (!LauncherSettings.ShowWindowLauncher.Value)
			{
				this.LauncherPopup.IsOpen = false;
				return;
			}

			if (this.LauncherPopup.IsOpen) return;

			(this.DataContext as ViewModels.Contents.OverviewViewModel)?.Launcher.RefreshAvailability();
			this.LauncherPopup.IsOpen = true;
		}
	}
}
