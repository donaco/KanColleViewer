using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Grabacr07.KanColleViewer.Views.Controls;
using MetroTrilithon.Mvvm;
using Microsoft.Xaml.Behaviors;

namespace Grabacr07.KanColleViewer.Views.Behaviors
{
	public class ZoomAction : Behavior<KanColleHost>
	{
		#region ViewModel 依存関係プロパティ

		public WindowViewModel ViewModel
		{
			get { return (WindowViewModel)this.GetValue(ViewModelProperty); }
			set { this.SetValue(ViewModelProperty, value); }
		}
		public static readonly DependencyProperty ViewModelProperty =
			DependencyProperty.Register(nameof(ViewModel), typeof(WindowViewModel), typeof(ZoomAction), new UIPropertyMetadata(null, OnViewModelChanged));

		private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var action = (ZoomAction)d;
			if (e.OldValue is WindowViewModel old) old.ZoomRequested -= action.OnZoomRequested;
			if (e.NewValue is WindowViewModel vm) vm.ZoomRequested += action.OnZoomRequested;
		}

		#endregion

		protected override void OnDetaching()
		{
			if (this.ViewModel != null) this.ViewModel.ZoomRequested -= this.OnZoomRequested;
			base.OnDetaching();
		}

		private void OnZoomRequested(object sender, EventArgs e)
		{
			this.AssociatedObject?.ApplySize();
		}
	}
}

