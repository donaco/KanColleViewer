using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq; // 追加
using System.Windows; // 追加

namespace Grabacr07.KanColleWrapper
{
	/// <summary>
	/// 母港を表します。
	/// </summary>
	public class Homeport : Notifier
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

		// UI スレッドへ安全に実行するヘルパー
		private static void RunOnUi(Action action)
		{
			try
			{
				if (Application.Current != null && Application.Current.Dispatcher != null)
				{
					Application.Current.Dispatcher.BeginInvoke(action);
				}
				else
				{
					action();
				}
			}
			catch
			{
				try { action(); } catch { }
			}
		}

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

		internal Homeport(KanColleProxy proxy)
		{
			this.Materials = new Materials(proxy);
			this.Itemyard = new Itemyard(this, proxy);
			this.Organization = new Organization(this, proxy);
			this.Repairyard = new Repairyard(this, proxy);
			this.Dockyard = new Dockyard(proxy);
			this.Quests = new Quests(proxy);

			// 診断ログ: コンストラクタ呼び出しとインスタンス識別子を記録
			try
			{
				var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
				Directory.CreateDirectory(logDir);
				var path = Path.Combine(logDir, "client_updates.log");
				File.AppendAllText(path, $"{DateTime.Now:O} Homeport.ctor invoked. instanceHash={this.GetHashCode()} proxyHash={(proxy?.GetHashCode() ?? 0)}\n");
			}
			catch { }

			// port は UI スレッドで反映する
			proxy.api_port.TryParse<kcsapi_port>().Subscribe(x =>
			{
				// 追加診断ログ: api_port 受信（UI スレッドに渡す前に記録）
				try
				{
					var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
					Directory.CreateDirectory(logDir);
					var path = Path.Combine(logDir, "client_updates.log");

					var ships = x.Data.api_ship?.Length ?? 0;
					var ndocks = x.Data.api_ndock?.Length ?? 0;
					var decks = x.Data.api_deck_port?.Length ?? 0;
					var materials = x.Data.api_material?.Length ?? 0;
					File.AppendAllText(path, $"{DateTime.Now:O} Homeport.api_port received. instanceHash={this.GetHashCode()} ships={ships} ndocks={ndocks} decks={decks} materials={materials}\n");
				}
				catch { }

				RunOnUi(() =>
				{
					this.UpdateAdmiral(x.Data.api_basic);
					this.Organization.Update(x.Data.api_ship);
					this.Repairyard.Update(x.Data.api_ndock);
					this.Organization.Update(x.Data.api_deck_port);
					this.Organization.Combined = x.Data.api_combined_flag != 0;
					this.Materials.Update(x.Data.api_material);
				});
			});

			// 個別 basic も UI スレッドで反映
			proxy.api_get_member_basic.TryParse<kcsapi_basic>().Subscribe(x =>
			{
				// 診断ログ: api_get_member_basic を受信
				try
				{
					var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
					Directory.CreateDirectory(logDir);
					var path = Path.Combine(logDir, "client_updates.log");
					File.AppendAllText(path, $"{DateTime.Now:O} Homeport.api_get_member_basic received. instanceHash={this.GetHashCode()}\n");
				}
				catch { }

				RunOnUi(() => this.UpdateAdmiral(x.Data));
			});

			proxy.api_req_member_updatecomment.TryParse().Subscribe(this.UpdateComment);
		}


		internal void UpdateAdmiral(kcsapi_basic data)
		{
			// 診断ログ追加
			try
			{
				var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grabacr.net", "KanColleViewer", "logs");
				Directory.CreateDirectory(logDir);
				var path = Path.Combine(logDir, "client_updates.log");

				string preview;
				try
				{
					var json = JsonConvert.SerializeObject(data);
					preview = json?.Length > 1000 ? json.Substring(0, 1000) + "..." : json;
				}
				catch
				{
					preview = "(serialize failed)";
				}

				File.AppendAllText(path, $"{DateTime.Now:O} Homeport.UpdateAdmiral invoked. data preview: {preview}\n\n");
			}
			catch { /* swallow */ }

			this.Admiral = new Admiral(data);
		}

		private void UpdateComment(SvData data)
		{
			if (data == null || !data.IsSuccess) return;

			try
			{
				this.Admiral.Comment = data.Request["api_cmt"];
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("艦隊名の変更に失敗しました: {0}", ex);
			}
		}

		internal void StartConditionCount()
		{
			//Observable.Timer(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3))
		}

	}
}
