using System;
using System.Windows;
using MetroTrilithon.Mvvm;
using Microsoft.Xaml.Behaviors;

namespace Grabacr07.KanColleViewer.Views.Behaviors
{
	/// <summary>
	/// <see cref="WindowViewModel"/> の Close/Activate/WindowState イベントを Window に反映するビヘイビアーです。
	/// </summary>
	public class WindowActionBehavior : Behavior<Window>
	{
		#region ViewModel 依存関係プロパティ

		public WindowViewModel ViewModel
		{
			get { return (WindowViewModel)this.GetValue(ViewModelProperty); }
			set { this.SetValue(ViewModelProperty, value); }
		}
		public static readonly DependencyProperty ViewModelProperty =
			DependencyProperty.Register(nameof(ViewModel), typeof(WindowViewModel), typeof(WindowActionBehavior), new UIPropertyMetadata(null, OnViewModelChanged));

		private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var b = (WindowActionBehavior)d;
			if (e.OldValue is WindowViewModel old)
			{
				old.CloseRequested -= b.OnCloseRequested;
				old.ActivateRequested -= b.OnActivateRequested;
			}
			if (e.NewValue is WindowViewModel vm)
			{
				vm.CloseRequested += b.OnCloseRequested;
				vm.ActivateRequested += b.OnActivateRequested;
			}
		}

		#endregion

		protected override void OnDetaching()
		{
			if (this.ViewModel != null)
			{
				this.ViewModel.CloseRequested -= this.OnCloseRequested;
				this.ViewModel.ActivateRequested -= this.OnActivateRequested;
			}
			base.OnDetaching();
		}

		private void OnCloseRequested(object sender, EventArgs e)
		{
			this.AssociatedObject?.Close();
		}

		private void OnActivateRequested(object sender, EventArgs e)
		{
			var w = this.AssociatedObject;
			if (w == null) return;
			if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
			w.Activate();
		}
	}
}
