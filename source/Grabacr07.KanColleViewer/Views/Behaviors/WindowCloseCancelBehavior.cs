using System.Windows;
using MetroTrilithon.Mvvm;
using Microsoft.Xaml.Behaviors;

namespace Grabacr07.KanColleViewer.Views.Behaviors
{
	/// <summary>
	/// ウィンドウの Close キャンセル処理を ViewModel と連携させるビヘイビアーです。
	/// </summary>
	public class WindowCloseCancelBehavior : Behavior<Window>
	{
		#region ViewModel 依存関係プロパティ

		public WindowViewModel ViewModel
		{
			get { return (WindowViewModel)this.GetValue(ViewModelProperty); }
			set { this.SetValue(ViewModelProperty, value); }
		}
		public static readonly DependencyProperty ViewModelProperty =
			DependencyProperty.Register(nameof(ViewModel), typeof(WindowViewModel), typeof(WindowCloseCancelBehavior), new UIPropertyMetadata(null));

		#endregion

		protected override void OnAttached()
		{
			base.OnAttached();
			this.AssociatedObject.Closing += this.OnClosing;
			this.AssociatedObject.Closed += this.OnClosed;
		}

		protected override void OnDetaching()
		{
			this.AssociatedObject.Closing -= this.OnClosing;
			this.AssociatedObject.Closed -= this.OnClosed;
			base.OnDetaching();
		}

		private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			var vm = this.ViewModel;
			if (vm == null) return;

			if (!vm.CanClose)
			{
				e.Cancel = true;
				vm.CloseCanceledCallback();
			}
		}

		private void OnClosed(object sender, System.EventArgs e)
		{
			var vm = this.ViewModel ?? (this.AssociatedObject?.DataContext as WindowViewModel);
			vm?.Dispose();
		}
	}
}
