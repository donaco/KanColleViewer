using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Properties;
using Grabacr07.KanColleViewer.ViewModels.Catalogs;
using Grabacr07.KanColleViewer.ViewModels.Contents.Launcher;
using Grabacr07.KanColleViewer.ViewModels.Settings;
using Grabacr07.KanColleViewer.Views.Catalogs;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.ViewModels.Contents
{
	public class OverviewViewModel : TabItemViewModel
	{
		private ShipCatalogWindowViewModel? shipCatalog;
		private SlotItemCatalogViewModel? slotItemCatalog;
		public override string Name
		{
			get { return Resources.IntegratedView; }
			protected set { throw new NotImplementedException(); }
		}

		public InformationViewModel Content { get; }

		/// <summary>
		/// 別ウィンドウをまとめて開閉するウィンドウランチャーを取得します。
		/// </summary>
		public LauncherViewModel Launcher { get; }


		public OverviewViewModel(InformationViewModel owner)
		{
			this.Content = owner;
			this.Launcher = new LauncherViewModel(this).AddTo(this);
		}


		public void Jump(string tabName)
		{
			TabItemViewModel? target = null;

			switch (tabName)
			{
				case "Fleets":
					target = this.Content.Fleets;
					break;
				case "Expeditions":
					target = this.Content.Expeditions;
					break;
				case "Quests":
					target = this.Content.Quests;
					break;
				case "Repairyard":
					target = this.Content.Shipyard;
					break;
				case "Dockyard":
					target = this.Content.Shipyard;
					break;
			}

			if (target == null)
			{
				return;
			}

			foreach (var tab in this.Content.TabItems)
			{
				tab.IsSelected = false;
			}

			foreach (var tab in this.Content.SystemTabItems)
			{
				tab.IsSelected = false;
			}

			target.IsSelected = true;
			this.Content.SelectedItem = target;
		}

		public void ShowWindowLauncherSettings()
		{
			foreach (var tab in this.Content.TabItems)
			{
				tab.IsSelected = false;
			}

			foreach (var tab in this.Content.SystemTabItems)
			{
				tab.IsSelected = false;
			}

			var settings = SettingsViewModel.Instance;
			settings.SelectWindowSettings();
			settings.IsSelected = true;
			this.Content.SelectedItem = settings;
		}

		public void ShowShipCatalog()
		{
           if (this.shipCatalog == null || this.shipCatalog.IsClosed)
           {
				this.shipCatalog = new ShipCatalogWindowViewModel();
				WindowService.Current.MainWindow.Transition(this.shipCatalog, typeof(ShipCatalogWindow));
			    return;
			}
			
			this.shipCatalog.Activate();
		}

		public void ShowSlotItemCatalog()
		{
           if (this.slotItemCatalog == null || this.slotItemCatalog.IsClosed)
			{
				this.slotItemCatalog = new SlotItemCatalogViewModel();
				WindowService.Current.MainWindow.Transition(this.slotItemCatalog, typeof(SlotItemCatalogWindow));
			    return;
			}
			
			this.slotItemCatalog.Activate();
		}

		public void CloseShipCatalog()
		{
			if (this.shipCatalog != null && !this.shipCatalog.IsClosed)
			{
				this.shipCatalog.Close();
			}
		}

		public void CloseSlotItemCatalog()
		{
			if (this.slotItemCatalog != null && !this.slotItemCatalog.IsClosed)
			{
				this.slotItemCatalog.Close();
			}
		}
	}
}
