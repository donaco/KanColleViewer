using System;
using System.Windows;
using System.Windows.Shell;
using MetroTrilithon.Mvvm;
using Microsoft.Xaml.Behaviors;

namespace Grabacr07.KanColleViewer.Views.Behaviors
{
	/// <summary>
	/// <see cref="WindowViewModel.TaskbarUpdateRequested"/> イベントを受け取り、タスクバーの進捗を更新するビヘイビアーです。
	/// </summary>
	public class TaskbarBehavior : Behavior<Window>
	{
		#region ViewModel 依存関係プロパティ

		public WindowViewModel ViewModel
		{
			get { return (WindowViewModel)this.GetValue(ViewModelProperty); }
			set { this.SetValue(ViewModelProperty, value); }
		}
		public static readonly DependencyProperty ViewModelProperty =
			DependencyProperty.Register(nameof(ViewModel), typeof(WindowViewModel), typeof(TaskbarBehavior), new UIPropertyMetadata(null, OnViewModelChanged));

		private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var b = (TaskbarBehavior)d;
			if (e.OldValue is WindowViewModel old) old.TaskbarUpdateRequested -= b.OnTaskbarUpdateRequested;
			if (e.NewValue is WindowViewModel vm) vm.TaskbarUpdateRequested += b.OnTaskbarUpdateRequested;
		}

		#endregion

		protected override void OnDetaching()
		{
			if (this.ViewModel != null) this.ViewModel.TaskbarUpdateRequested -= this.OnTaskbarUpdateRequested;
			base.OnDetaching();
		}

		private void OnTaskbarUpdateRequested(object sender, TaskbarUpdateEventArgs e)
		{
			var w = this.AssociatedObject;
			if (w == null) return;
			if (w.TaskbarItemInfo == null) w.TaskbarItemInfo = new TaskbarItemInfo();
			w.TaskbarItemInfo.ProgressState = e.State;
			w.TaskbarItemInfo.ProgressValue = e.Value;
		}
	}
}
