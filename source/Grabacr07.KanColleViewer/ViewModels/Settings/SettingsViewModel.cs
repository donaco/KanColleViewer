using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Properties;
using Grabacr07.KanColleViewer.ViewModels.Composition;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.ViewModels.Settings
{
	public class SettingsViewModel : TabItemViewModel
	{
		public static SettingsViewModel Instance { get; } = new SettingsViewModel();


		public override string Name
		{
			get { return Resources.Settings; }
			protected set { throw new NotImplementedException(); }
		}


		public ScreenshotSettingsViewModel ScreenshotSettings { get; }

		public WindowSettingsViewModel WindowSettings { get; }

		public NetworkSettingsViewModel NetworkSettings { get; }

		public UserStyleSheetSettingsViewModel UserStyleSheetSettings { get; }

		public NavigatorViewModel? Navigator { get; set; }

		public BrowserZoomFactor BrowserZoomFactor { get; }

		public IReadOnlyCollection<CultureViewModel> Cultures { get; }

		public IReadOnlyCollection<BindableTextViewModel> Libraries { get; }

		public IReadOnlyCollection<UpdateCheckModeItemViewModel> UpdateCheckModes { get; private set; } = Array.Empty<UpdateCheckModeItemViewModel>();

		public List<PluginViewModel> LoadedPlugins => new List<PluginViewModel>(
			PluginService.Current.Plugins.Select(x => new PluginViewModel(x)));

		public List<LoadFailedPluginViewModel> FailedPlugins => new List<LoadFailedPluginViewModel>(
			PluginService.Current.FailedPlugins.Select(x => new LoadFailedPluginViewModel(x)));

		#region UpdateStatusText 変更通知プロパティ

		private string _UpdateStatusText = string.Empty;
		private string? updateStatusResourceKey;
		private string? updateStatusVersion;

		public string UpdateStatusText
		{
			get { return this._UpdateStatusText; }
			set
			{
				if (this._UpdateStatusText != value)
				{
					this._UpdateStatusText = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsUpdateAvailable 変更通知プロパティ

		private bool _IsUpdateAvailable;

		public bool IsUpdateAvailable
		{
			get { return this._IsUpdateAvailable; }
			set
			{
				if (this._IsUpdateAvailable != value)
				{
					this._IsUpdateAvailable = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region UpdateUri 変更通知プロパティ

		private Uri _UpdateUri = null!;

		public Uri UpdateUri
		{
			get { return this._UpdateUri; }
			set
			{
				if (this._UpdateUri != value)
				{
					this._UpdateUri = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region CheckForUpdateCommand コマンド

		private RelayCommand? _CheckForUpdateCommand;

		public RelayCommand CheckForUpdateCommand
			=> this._CheckForUpdateCommand ??= new RelayCommand(this.CheckForUpdate);

		#endregion

		#region ViewRangeSettingsCollection 変更通知プロパティ

		private List<ICalcViewRange> _ViewRangeSettingsCollection = new();

		public List<ICalcViewRange> ViewRangeSettingsCollection
		{
			get { return this._ViewRangeSettingsCollection; }
			set
			{
				if (this._ViewRangeSettingsCollection != value)
				{
					this._ViewRangeSettingsCollection = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region SelectedViewRangeCalcType 変更通知プロパティ

		private ICalcViewRange _SelectedViewRangeCalcType = null!;

		public ICalcViewRange SelectedViewRangeCalcType
		{
			get { return this._SelectedViewRangeCalcType; }
			set
			{
				if (value == null) return;

				if (this._SelectedViewRangeCalcType != value)
				{
					this._SelectedViewRangeCalcType = value;
					KanColleSettings.ViewRangeCalcType.Value = value.Id;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		private SettingsViewModel()
		{
			PluginService.Current.PluginsReloaded += () =>
			{
				this.RaisePropertyChanged(nameof(this.LoadedPlugins));
				this.RaisePropertyChanged(nameof(this.FailedPlugins));
			};

			this.ScreenshotSettings = new ScreenshotSettingsViewModel().AddTo(this);
			this.WindowSettings = new WindowSettingsViewModel().AddTo(this);
			this.NetworkSettings = new NetworkSettingsViewModel().AddTo(this);
			this.UserStyleSheetSettings = new UserStyleSheetSettingsViewModel().AddTo(this);

			this.BrowserZoomFactor = new BrowserZoomFactor { Current = GeneralSettings.BrowserZoomFactor };
			this.BrowserZoomFactor
				.Subscribe(nameof(this.BrowserZoomFactor.Current), () => GeneralSettings.BrowserZoomFactor.Value = this.BrowserZoomFactor.Current)
				.AddTo(this);
			GeneralSettings.BrowserZoomFactor.Subscribe(x => this.BrowserZoomFactor.Current = x).AddTo(this);

			this.Cultures = new[] { new CultureViewModel { DisplayName = "(auto)" } }
				.Concat(ResourceService.Current.SupportedCultures
					.Select(x => new CultureViewModel { DisplayName = x.EnglishName, Name = x.Name })
					.OrderBy(x => x.DisplayName))
				.ToList();

			this.Libraries = ProductInfo.Libraries.Aggregate(
				new List<BindableTextViewModel>(),
				(list, lib) =>
				{
					list.Add(new BindableTextViewModel { Text = list.Count == 0 ? "Build with " : ", " });
					list.Add(new HyperlinkViewModel { Text = lib.Name.Replace(' ', Convert.ToChar(160)), Uri = lib.Url });
					// ☝プロダクト名の途中で改行されないように、space を non-break space に置き換えてあげてるんだからねっっ
					return list;
				});

			this.ReloadLocalizedSettings();
			System.ComponentModel.PropertyChangedEventHandler cultureChangedHandler = (s, e) =>
			{
				if (e.PropertyName != nameof(ResourceService.Resources)) return;

				this.ReloadLocalizedSettings();
				this.RefreshUpdateStatus();
			};
			ResourceService.Current.PropertyChanged += cultureChangedHandler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => ResourceService.Current.PropertyChanged -= cultureChangedHandler));
		}

		private void ReloadLocalizedSettings()
		{
			var selectedId = this.SelectedViewRangeCalcType?.Id ?? KanColleSettings.ViewRangeCalcType;
			this.UpdateCheckModes = new[]
			{
				new UpdateCheckModeItemViewModel { Display = ResourceService.Current["Ui_UpdateCheck_Mode_Manual"], Value = false },
				new UpdateCheckModeItemViewModel { Display = ResourceService.Current["Ui_UpdateCheck_Mode_Automatic"], Value = true },
			};
			this.RaisePropertyChanged(nameof(this.UpdateCheckModes));

			this.ViewRangeSettingsCollection = ViewRangeCalcLogic.Logics
				.Select(logic => (ICalcViewRange)new LocalizedViewRange(logic))
				.ToList();
			this.SelectedViewRangeCalcType = this.ViewRangeSettingsCollection
				.FirstOrDefault(x => x.Id == selectedId)
				?? this.ViewRangeSettingsCollection.First();
		}


		public void Initialize()
		{
			this.WindowSettings.Initialize();
			this.NetworkSettings.Initialize();
			this.UserStyleSheetSettings.Initialize();
			this.RaisePropertyChanged(nameof(this.LoadedPlugins));
			this.RaisePropertyChanged(nameof(this.FailedPlugins));
		}


		private async void CheckForUpdate()
		{
			this.IsUpdateAvailable = false;
			this.SetUpdateStatus("Ui_UpdateCheck_Status_Checking");

			try
			{
				var result = await UpdateChecker.CheckAsync();

				if (result.IsUpdateAvailable)
				{
					this.IsUpdateAvailable = true;
					this.SetUpdateStatus("Ui_UpdateCheck_Status_Available", result.LatestVersion);

					if (Uri.TryCreate(result.ReleaseUrl, UriKind.Absolute, out var uri))
					{
						this.UpdateUri = uri;
					}
				}
				else
				{
					this.SetUpdateStatus("Ui_UpdateCheck_Status_UpToDate");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
				this.SetUpdateStatus("Ui_UpdateCheck_Status_Failed");
			}
		}

		private void SetUpdateStatus(string resourceKey, string? version = null)
		{
			this.updateStatusResourceKey = resourceKey;
			this.updateStatusVersion = version;
			this.RefreshUpdateStatus();
		}

		private void RefreshUpdateStatus()
		{
			if (this.updateStatusResourceKey == null) return;

			var format = ResourceService.Current[this.updateStatusResourceKey];
			this.UpdateStatusText = this.updateStatusVersion == null
				? format
				: string.Format(format, this.updateStatusVersion);
		}
	}

	public class UpdateCheckModeItemViewModel : ItemViewModel
	{
		public string Display { get; set; } = string.Empty;

		public bool Value { get; set; }
	}

	internal sealed class LocalizedViewRange : ICalcViewRange
	{
		private readonly ICalcViewRange source;
		private readonly string? resourceKey;

		public string Id => this.source.Id;
		public string Name => this.resourceKey == null ? this.source.Name : ResourceService.Current[$"Ui_ViewRange_{this.resourceKey}_Name"];
		public string Description => this.resourceKey == null ? this.source.Description : ResourceService.Current[$"Ui_ViewRange_{this.resourceKey}_Description"];
		public bool HasCombinedSettings => this.source.HasCombinedSettings;

		public LocalizedViewRange(ICalcViewRange source)
		{
			this.source = source;
			this.resourceKey = source.Id switch
			{
				"KanColleViewer.Type1" => "Type1",
				"KanColleViewer.Type2" => "Type2",
				"KanColleViewer.Type3" => "Type3",
				"KanColleViewer.Type4" => "Type4",
				_ => null,
			};
		}

		public double Calc(Fleet[] fleets) => this.source.Calc(fleets);
	}
}
