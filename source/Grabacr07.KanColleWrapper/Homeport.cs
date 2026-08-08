using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Grabacr07.KanColleWrapper
{
	/// <summary>
	/// 母港を表します。
	/// </summary>
	public class Homeport : Notifier, IDisposable
	{
		/// <summary>
		/// 艦隊の編成状況にアクセスできるようにします。
		/// </summary>
		public Organization Organization { get; }

		/// <summary>
		/// 資源および資材の保有状況にアクセスできるようにします。
		/// </summary>
		public Materials Materials { get; }

		/// <summary>
		/// 装備や消費アイテムの保有状況にアクセスできるようにします。
		/// </summary>
		public Itemyard Itemyard { get; }

		/// <summary>
		/// 複数の建造ドックを持つ工廠を取得します。
		/// </summary>
		public Dockyard Dockyard { get; }

		/// <summary>
		/// 複数の入渠ドックを持つ工廠を取得します。
		/// </summary>
		public Repairyard Repairyard { get; }

		/// <summary>
		/// 任務情報を取得します。
		/// </summary>
		public Quests Quests { get; }

		/// <summary>
		/// 基地航空隊（航空隊）の情報を取得します。
		/// </summary>
		public AirBases AirBases { get; }

		// UI スレッドへ安全に実行するヘルパー（共通実装は HandlerHelper に統一）
		private static void RunOnUi(Action action)
			=> Grabacr07.KanColleWrapper.Handlers.HandlerHelper.RunOnUi(action);

		#region Admiral 変更通知プロパティ

		private Admiral _Admiral;

		/// <summary>
		/// 現在ログインしている提督を取得します。
		/// <see cref="INotifyPropertyChanged.PropertyChanged"/> イベントによる変更通知をサポートします。
		/// </summary>
		public Admiral Admiral
		{
			get { return this._Admiral; }
			private set
			{
				if (this._Admiral != value)
				{
					this._Admiral = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		internal Homeport()
		{
			this.Materials = new Materials();
			this.Itemyard = new Itemyard(this);
			this.Organization = new Organization(this);
			this.Repairyard = new Repairyard(this);
			this.Dockyard = new Dockyard();
			this.Quests = new Quests();
			this.AirBases = new AirBases();
		}

		public void Dispose()
		{
			this.Materials?.Dispose();
			this.Itemyard?.Dispose();
			this.Organization?.Dispose();
			this.Repairyard?.Dispose();
			this.Dockyard?.Dispose();
			this.Quests?.Dispose();
		}

		internal void UpdateAdmiral(kcsapi_basic data)
		{
			this.Admiral = new Admiral(data);
		}

		internal void UpdateComment(string comment)
		{
			if (this.Admiral == null) return;
			this.Admiral.Comment = comment;
		}

		internal void StartConditionCount()
		{
			//Observable.Timer(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3))
		}

	}
}
