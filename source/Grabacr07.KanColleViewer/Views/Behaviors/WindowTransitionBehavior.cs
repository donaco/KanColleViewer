using System;
using System.Windows;
using MetroTrilithon.Mvvm;
using Microsoft.Xaml.Behaviors;

namespace Grabacr07.KanColleViewer.Views.Behaviors
{
	/// <summary>
	/// <see cref="WindowViewModel.TransitionRequested"/> イベントを受け取り、新しいウィンドウを開くビヘイビアーです。
	/// </summary>
	public class WindowTransitionBehavior : Behavior<Window>
	{
		#region ViewModel 依存関係プロパティ

		public WindowViewModel ViewModel
		{
			get { return (WindowViewModel)this.GetValue(ViewModelProperty); }
			set { this.SetValue(ViewModelProperty, value); }
		}
		public static readonly DependencyProperty ViewModelProperty =
			DependencyProperty.Register(nameof(ViewModel), typeof(WindowViewModel), typeof(WindowTransitionBehavior), new UIPropertyMetadata(null, OnViewModelChanged));

		private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var b = (WindowTransitionBehavior)d;
			if (e.OldValue is WindowViewModel old) old.TransitionRequested -= b.OnTransitionRequested;
			if (e.NewValue is WindowViewModel vm) vm.TransitionRequested += b.OnTransitionRequested;
		}

		#endregion

		protected override void OnDetaching()
		{
			if (this.ViewModel != null) this.ViewModel.TransitionRequested -= this.OnTransitionRequested;
			base.OnDetaching();
		}

		private void OnTransitionRequested(object sender, TransitionRequestedEventArgs e)
		{
			var window = (Window)Activator.CreateInstance(e.WindowType);
			window.DataContext = e.ViewModel;

			if (e.IsOwned) window.Owner = this.AssociatedObject;

			// ViewModel のイベントで閉じる
			if (e.ViewModel is WindowViewModel wvm)
			{
				wvm.CloseRequested += (s, ev) =>
				{
					window.Close();
				};
				wvm.ActivateRequested += (s, ev) =>
				{
					window.Activate();
				};
			}

			window.Show();
		}
	}
}
