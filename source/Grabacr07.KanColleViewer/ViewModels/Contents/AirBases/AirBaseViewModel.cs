using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper.Models;
using Livet;
using Livet.EventListeners;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.AirBases
{
	/// <summary>
	/// 個別の航空隊（海域ごと）の ViewModel
	/// </summary>
	public class AirBaseViewModel : ViewModel
	{
		private readonly AirBase source;

		#region AreaId 変更通知プロパティ

		private int _AreaId;

		public int AreaId
		{
			get { return this._AreaId; }
			set
			{
				if (this._AreaId != value)
				{
					this._AreaId = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region AreaName 変更通知プロパティ

		private string _AreaName;

		public string AreaName
		{
			get { return this._AreaName; }
			set
			{
				if (this._AreaName != value)
				{
					this._AreaName = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region MaintenanceLevel 変更通知プロパティ

		private int _MaintenanceLevel;

		public int MaintenanceLevel
		{
			get { return this._MaintenanceLevel; }
			set
			{
				if (this._MaintenanceLevel != value)
				{
					this._MaintenanceLevel = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region BaseCount 変更通知プロパティ

		private int _BaseCount;

		public int BaseCount
		{
			get { return this._BaseCount; }
			set
			{
				if (this._BaseCount != value)
				{
					this._BaseCount = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region AirBaseNames 航空隊名リスト

		private string[] _AirBaseNames;

		public string[] AirBaseNames
		{
			get { return this._AirBaseNames; }
			set
			{
				if (this._AirBaseNames != value)
				{
					this._AirBaseNames = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region AirBaseInfos 基地情報ViewModel配列

		private AirBaseInfoViewModel[] _AirBaseInfos;

		public AirBaseInfoViewModel[] AirBaseInfos
		{
			get { return this._AirBaseInfos; }
			set
			{
				if (this._AirBaseInfos != value)
				{
					this._AirBaseInfos = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region ActionKind 航空隊 状態

		private int _ActionKind;

		public int ActionKind
		{
			get { return this._ActionKind; }
			set
			{
				if (this._ActionKind != value)
				{
					this._ActionKind = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region ActionKindText 航空隊 状態名

		private string _ActionKindText;

		public string ActionKindText
		{
			get { return this._ActionKindText; }
			set
			{
				if (this._ActionKindText != value)
				{
					this._ActionKindText = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion


		public AirBaseViewModel(AirBase airBase)
		{
			this.source = airBase;

			this.UpdateProperties();

			var listener = new PropertyChangedEventListener(airBase)
			{
				(sender, args) => this.UpdateProperties(),
			};
			this.CompositeDisposable.Add(listener);
		}

		private void UpdateProperties()
		{
			this.AreaId = this.source.AreaId;
			this.AreaName = this.source.AreaName;
			this.MaintenanceLevel = this.source.MaintenanceLevel;
			this.BaseCount = this.source.BaseCount;
			this.AirBaseNames = this.source.AirBaseNames;
			this.ActionKind = this.source.ActionKind;
			this.ActionKindText = GetActionKindText(this.source.ActionKind);

			// 各基地情報を ViewModel に変換
			this.AirBaseInfos = this.source.AirBaseInfos?.Select(x => new AirBaseInfoViewModel(
				name: x.Name,
				actionKind: x.ActionKind,
				distance: x.Distance,
				equipmentSlotIds: x.EquipmentSlotIds,
				equipmentIconTypes: x.EquipmentIconTypes,
				equipmentNames: x.EquipmentNames,
				equipmentLevels: x.EquipmentLevels,
				equipmentAlvs: x.EquipmentAlvs
			)).ToArray() ?? new AirBaseInfoViewModel[0];
		}

		private static string GetActionKindText(int actionKind)
		{
			switch (actionKind)
			{
				case 1: return "出撃";
				case 2: return "防空";
				case 3: return "退避";
				case 4: return "休息";
				case 0: return "待機";
				default: return "不明";
			}
		}

		public override string ToString()
		{
			return $"{this.AreaName}（{this.BaseCount} 基地）";
		}
	}
}
