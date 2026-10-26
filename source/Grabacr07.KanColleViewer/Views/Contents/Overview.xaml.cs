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
		/// 「データ」にマウスを合わせたとき、ウィンドウランチャーを表示します。
		/// </summary>
		private void DataHeader_MouseEnter(object sender, MouseEventArgs e)
		{
			if (this.LauncherPopup.IsOpen) return;

			(this.DataContext as ViewModels.Contents.OverviewViewModel)?.Launcher.RefreshAvailability();
			this.LauncherPopup.IsOpen = true;
		}
	}
}
