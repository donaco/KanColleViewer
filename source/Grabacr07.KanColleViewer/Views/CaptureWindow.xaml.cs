using System.Windows;
using Grabacr07.KanColleViewer.Models.Cef;

namespace Grabacr07.KanColleViewer.Views
{
	public partial class CaptureWindow : Window
	{
		public CaptureWindow()
		{
			InitializeComponent();
			this.DataContext = CaptureLogService.Instance;
		}

		private void Clear_Click(object sender, RoutedEventArgs e)
		{
			CaptureLogService.Instance.Clear();
		}
	}
}
