using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using MetroTrilithon.Linq;
using MetroTrilithon.Mvvm;

using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
namespace Grabacr07.KanColleViewer.ViewModels.Settings
{
	public class WindowSettingsViewModel : ViewModelBase
	{
		private KanColleWindowSettings? settings;

		public IReadOnlyCollection<DisplayViewModel<ExitConfirmationType>> ExitConfirmationTypes { get; private set; } = Array.Empty<DisplayViewModel<ExitConfirmationType>>();

		private IReadOnlyCollection<DisplayViewModel<string>> _TaskbarProgressFeatures = Array.Empty<DisplayViewModel<string>>();

		public IReadOnlyCollection<DisplayViewModel<string>> TaskbarProgressFeatures
		{
			get { return this._TaskbarProgressFeatures; }
			private set
			{
				if (this._TaskbarProgressFeatures != value)
				{
					this._TaskbarProgressFeatures = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#region IsSplit 変更通知プロパティ

		private bool _IsSplit;

		public bool IsSplit
		{
			get { return this._IsSplit; }
			set
			{
				if (this._IsSplit != value)
				{
					this._IsSplit = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region Dock 変更通知プロパティ

		private Dock _Dock;

		public Dock Dock
		{
			get { return this._Dock; }
			set
			{
				if (this._Dock != value)
				{
					this._Dock = value;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.DockLeft));
					this.RaisePropertyChanged(nameof(this.DockTop));
					this.RaisePropertyChanged(nameof(this.DockRight));
					this.RaisePropertyChanged(nameof(this.DockBottom));
				}
			}
		}

		public bool DockLeft => this.Dock == Dock.Left;
		public bool DockTop => this.Dock == Dock.Top;
		public bool DockRight => this.Dock == Dock.Right;
		public bool DockBottom => this.Dock == Dock.Bottom;

		#endregion

		public WindowSettingsViewModel()
		{
			this.ReloadExitConfirmationTypes();
			this.ReloadTaskbarProgressFeatures();
			KanColleViewer.Composition.PluginService.Current.PluginsReloaded += this.ReloadTaskbarProgressFeatures;

			System.ComponentModel.PropertyChangedEventHandler handler = (s, e) =>
			{
				if (e.PropertyName != nameof(ResourceService.Resources)) return;

				this.ReloadExitConfirmationTypes();
				this.ReloadTaskbarProgressFeatures();
			};
			ResourceService.Current.PropertyChanged += handler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => ResourceService.Current.PropertyChanged -= handler));
		}

		private void ReloadExitConfirmationTypes()
		{
			this.ExitConfirmationTypes = new List<DisplayViewModel<ExitConfirmationType>>
			{
				DisplayViewModel.Create(ExitConfirmationType.None, ResourceService.Current["Ui_Window_ExitConfirmation_None"]),
				DisplayViewModel.Create(ExitConfirmationType.InSortieOnly, ResourceService.Current["Ui_Window_ExitConfirmation_InSortieOnly"]),
				DisplayViewModel.Create(ExitConfirmationType.Always, ResourceService.Current["Ui_Window_ExitConfirmation_Always"]),
			};
			this.RaisePropertyChanged(nameof(this.ExitConfirmationTypes));
		}

		public void Initialize()
		{
			this.settings = SettingsHost.Instance<KanColleWindowSettings>();
			this.settings.IsSplit.Subscribe(x => this.IsSplit = x).AddTo(this);
			this.settings.Dock.Subscribe(x => this.Dock = x).AddTo(this);
		}

		private void ReloadTaskbarProgressFeatures()
		{
			this.TaskbarProgressFeatures = EnumerableEx
				.Return(GeneralSettings.TaskbarProgressSource.ToDefaultDisplay(ResourceService.Current["Ui_Window_TaskbarProgress_None"]))
				.Concat(TaskbarProgress.Features.ToDisplay(x => x.Id, x => x.Id switch
				{
					"C8BF00A6-9FD4-4CC4-8FC5-ECCC5675CDEB-1" => ResourceService.Current["Ui_Window_TaskbarProgress_Expedition"],
					"DA0E7091-F4A6-4467-9812-3C3E0DF946EA-1" => ResourceService.Current["Ui_Window_TaskbarProgress_FleetHp"],
					_ => x.DisplayName,
				}))
				.ToList();
		}

		public void SetDockSettings(Dock dock)
		{
			this.Dock = dock;
		}

		public void Apply()
		{
			if (this.settings != null)
			{
				this.settings.IsSplit.Value = this.IsSplit;
				this.settings.Dock.Value = this.Dock;
			}
		}

		public void Cancel()
		{
			if (this.settings != null)
			{
				this.IsSplit = this.settings.IsSplit;
				this.Dock = this.settings.Dock;
			}
		}
	}
}
