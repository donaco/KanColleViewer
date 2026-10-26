using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleWrapper;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.Launcher
{
	/// <summary>
	/// 別ウィンドウの表示/非表示をまとめて操作するウィンドウランチャーの ViewModel です。
	/// </summary>
	public class LauncherViewModel : ViewModelBase
	{
		// プラグインの [ツール] タブ名 (ITool.Name)
		private const string CounterToolName = "Counter";
		private const string MapHpToolName = "MapHP";
		private const string MasterViewToolName = "MasterView";
		private const string MaterialToolName = "Material";

		private readonly OverviewViewModel owner;

		/// <summary>
		/// ランチャーに表示する項目一覧を取得します。
		/// </summary>
		public IReadOnlyList<LauncherItemViewModel> Items { get; }

		public LauncherViewModel(OverviewViewModel owner)
		{
			this.owner = owner;

			this.Items = new List<LauncherItemViewModel>
			{
				// 艦娘一覧
				new LauncherItemViewModel(
					() => ResourceService.Current["Ui_Overview_ShipCatalog"],
					LauncherSettings.ShipCatalog,
					IsStarted,
					() => this.owner.ShowShipCatalog(),
					() => this.owner.CloseShipCatalog()),

				// 装備一覧
				new LauncherItemViewModel(
					() => ResourceService.Current["Ui_Overview_SlotItemCatalog"],
					LauncherSettings.SlotItemCatalog,
					IsStarted,
					() => this.owner.ShowSlotItemCatalog(),
					() => this.owner.CloseSlotItemCatalog()),

				// 任務一覧
				new LauncherItemViewModel(
					() => ResourceService.Current["Ui_Launcher_Quests"],
					LauncherSettings.Quests,
					IsStarted,
					() => this.owner.Content.Quests.ShowQuestWindow(),
					() => this.owner.Content.Quests.CloseQuestWindow()),

				// 艦隊詳細
				new LauncherItemViewModel(
					() => ResourceService.Current["Ui_Launcher_Fleets"],
					LauncherSettings.Fleets,
					() => IsStarted() && this.owner.Content.Fleets.Fleets.Length > 0,
					() => this.owner.Content.Fleets.ShowFleetWindow(),
					() => this.owner.Content.Fleets.CloseFleetWindow()),

				// 基地詳細
				new LauncherItemViewModel(
					() => ResourceService.Current["Ui_Launcher_AirBases"],
					LauncherSettings.AirBases,
					() => IsStarted() && !this.owner.Content.AirBases.IsEmpty && this.owner.Content.AirBases.SelectedAirBase != null,
					() => this.owner.Content.AirBases.ShowAirBaseWindow(),
					() => this.owner.Content.AirBases.CloseAirBaseWindow()),

				// Counter
				new LauncherItemViewModel(
					() => CounterToolName,
					LauncherSettings.Counter,
					() => GetPluginViewModel(CounterToolName, "viewModel") is { } vm
						&& PluginWindowInvoker.HasMethod(vm, "OpenPopupWindow"),
					() => PluginWindowInvoker.InvokeMethod(GetPluginViewModel(CounterToolName, "viewModel"), "OpenPopupWindow"),
					() => PluginWindowInvoker.InvokeMethod(GetPluginViewModel(CounterToolName, "viewModel"), "ClosePopupWindow")),

				// MapHP (攻略中の海域がない場合は表示しない)
				new LauncherItemViewModel(
					() => MapHpToolName,
					LauncherSettings.MapHp,
					() => GetPluginViewModel(MapHpToolName, "toolVm") is { } vm
						&& PluginWindowInvoker.HasMethod(vm, "OpenPopupWindow")
						&& PluginWindowInvoker.GetProperty(vm, "Maps") is Array maps
						&& maps.Length > 0,
					() => PluginWindowInvoker.InvokeMethod(GetPluginViewModel(MapHpToolName, "toolVm"), "OpenPopupWindow"),
					() => PluginWindowInvoker.InvokeMethod(GetPluginViewModel(MapHpToolName, "toolVm"), "ClosePopupWindow")),

				// MasterView (マスター データ未取得の場合は表示しない)
				new LauncherItemViewModel(
					() => MasterViewToolName,
					LauncherSettings.MasterView,
					() => IsStarted()
						&& KanColleClient.Current.Master != null
						&& GetPluginViewModel(MasterViewToolName, "portalViewModel") is { } vm
						&& PluginWindowInvoker.HasMethod(vm, "ShowPopupWindow"),
					() => PluginWindowInvoker.InvokeMethod(GetPluginViewModel(MasterViewToolName, "portalViewModel"), "ShowPopupWindow"),
					() => PluginWindowInvoker.CloseWindows(GetPluginViewModel(MasterViewToolName, "portalViewModel"), "MasterDataWindow")),

				// Material (資材データ未取得の場合は表示しない)
				new LauncherItemViewModel(
					() => MaterialToolName,
					LauncherSettings.Material,
					() => GetPluginViewModel(MaterialToolName, "viewModel") is { } vm
						&& PluginWindowInvoker.HasMethod(vm, "OpenPopupWindow")
						&& PluginWindowInvoker.GetProperty(PluginWindowInvoker.GetProperty(vm, "materialManager"), "IsAvailable") is true,
					OpenMaterialWindow,
					() => PluginWindowInvoker.CloseWindows(GetPluginViewModel(MaterialToolName, "viewModel"), "MaterialWindow")),
			};

			foreach (var item in this.Items)
			{
				item.AddTo(this);
			}
		}

		/// <summary>
		/// 各項目の表示可否を最新の状態に更新します。
		/// </summary>
		public void RefreshAvailability()
		{
			foreach (var item in this.Items)
			{
				item.Refresh();
			}
		}

		/// <summary>
		/// チェックされていて、表示可能な項目のウィンドウをまとめて開きます。
		/// </summary>
		public void ShowAll()
		{
			foreach (var item in this.Items.Where(x => x.IsChecked))
			{
				item.Open();
			}
		}

		/// <summary>
		/// 開いている項目のウィンドウをすべて閉じます。
		/// </summary>
		public void CloseAll()
		{
			foreach (var item in this.Items)
			{
				item.Close();
			}
		}


		private static bool IsStarted()
		{
			return KanColleClient.Current?.IsStarted == true;
		}

		/// <summary>
		/// プラグインが保持している ViewModel をリフレクションで取得します。
		/// </summary>
		private static object? GetPluginViewModel(string toolName, string fieldName)
		{
			return PluginWindowInvoker.GetField(PluginWindowInvoker.FindTool(toolName), fieldName);
		}

		/// <summary>
		/// Material は多重起動ガードがないため、既に開いている場合はアクティブにするだけにします。
		/// </summary>
		private static void OpenMaterialWindow()
		{
			var vm = GetPluginViewModel(MaterialToolName, "viewModel");
			var existing = PluginWindowInvoker.FindWindow(vm, "MaterialWindow");
			if (existing != null)
			{
				if (existing.WindowState == WindowState.Minimized)
				{
					existing.WindowState = WindowState.Normal;
				}
				existing.Activate();
				return;
			}

			PluginWindowInvoker.InvokeMethod(vm, "OpenPopupWindow");
		}
	}
}
