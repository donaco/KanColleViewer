using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleWrapper;

namespace Counter
{
	[Export(typeof(IPlugin))]
	[Export(typeof(ITool))]
	[Export(typeof(IRequestNotify))]
	[ExportMetadata("Guid", "65BE3E80-8EC1-41BD-85E0-78AEFD45A757")]
	[ExportMetadata("Title", "KanColleCounter")]
	[ExportMetadata("Description", "回数カウント機能や出撃履歴を提供します。")]
	[ExportMetadata("Version", "2.1.1")]
	[ExportMetadata("Author", "@Grabacr07")]
	public class KanColleCounter : IPlugin, ITool, IRequestNotify
	{
		private CounterViewModel viewModel;
		private bool _dataSaved;

		string ITool.Name => "Counter";

		object ITool.View => new CounterView { DataContext = this.viewModel, };

		public event EventHandler<NotifyEventArgs> NotifyRequested;

		public void Initialize()
		{
			try
			{
				var proxy = KanColleClient.Current?.Proxy;
				if (proxy == null)
				{
					System.Diagnostics.Debug.WriteLine("[Counter] KanColleProxy が取得できませんでした。プラグインは無効状態で起動します。");
					this.viewModel = new CounterViewModel
					{
						Counters = new ObservableCollection<CounterBase>(),
						SortieHistory = null,
					};
					return;
				}

				this.viewModel = new CounterViewModel
				{
					Counters = new ObservableCollection<CounterBase>
					{
						new SupplyCounter(proxy),
						new ItemDestroyCounter(proxy),
						new MissionCounter(proxy),
						new SortieCounter(proxy),
					},
					// --- 直近12件を表示(表示数を指定) ---
					SortieHistory = new SortieHistoryCounter(proxy, 12),
				};

				// --- 保存データを復元 ---
				this.RestoreSavedData();

				// --- 終了時の保存を2段構えで登録 ---
				// 1. Application.Exit（通常の終了）
				if (System.Windows.Application.Current != null)
				{
					System.Windows.Application.Current.Exit += (s, e) =>
					{
						this.SaveDataOnce();
						this.viewModel?.ClosePopupWindow();
					};
				}

				// 2. ProcessExit（Environment.Exit による強制終了にも対応）
				AppDomain.CurrentDomain.ProcessExit += (s, e) =>
				{
					this.SaveDataOnce();
				};
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Counter] 初期化エラー: {ex.Message}");
				this.viewModel = new CounterViewModel
				{
					Counters = new ObservableCollection<CounterBase>(),
					SortieHistory = null,
				};
			}
		}

		/// <summary>
		/// 保存データを読み込み、各カウンターに復元します。
		/// </summary>
		private void RestoreSavedData()
		{
			var data = CounterDataStore.Load();
			if (data == null) return;

			// 各カウンターの値を復元（Text をキーに照合）
			if (data.Counters != null && this.viewModel.Counters != null)
			{
				foreach (var counter in this.viewModel.Counters)
				{
					if (!string.IsNullOrEmpty(counter.Text) && data.Counters.TryGetValue(counter.Text, out var count))
					{
						counter.Count = count;
						System.Diagnostics.Debug.WriteLine($"[Counter] 復元: {counter.Text} = {count}");
					}
				}
			}

			// 海域ごとの出撃数を復元
			if (data.AreaCounts != null && this.viewModel.SortieHistory != null)
			{
				this.viewModel.SortieHistory.RestoreAreaCounts(data.AreaCounts);
			}

			// 戦闘履歴を復元
			if (data.History != null && this.viewModel.SortieHistory != null)
			{
				this.viewModel.SortieHistory.RestoreHistory(data.History);
			}

			// 設定項目の復元
			if (data.IsCounterEnabled.HasValue)
			{
				this.viewModel.IsCounterEnabled = data.IsCounterEnabled.Value;
			}
			if (data.IsSortieHistoryEnabled.HasValue)
			{
				this.viewModel.IsSortieHistoryEnabled = data.IsSortieHistoryEnabled.Value;
			}
			if (data.ShowAirSuperiority.HasValue)
			{
				this.viewModel.ShowAirSuperiority = data.ShowAirSuperiority.Value;
			}
			if (data.IsTopMost.HasValue)
			{
				this.viewModel.IsTopMost = data.IsTopMost.Value;
			}
			if (data.BossOnly.HasValue && this.viewModel.SortieHistory != null)
			{
				this.viewModel.SortieHistory.BossOnly = data.BossOnly.Value;
			}
		}

		/// <summary>
		/// 現在のカウンターデータを保存します（二重保存防止付き）。
		/// Application.Exit と ProcessExit の両方から呼ばれる可能性があるため、
		/// 一度だけ実行されるようにガードしています。
		/// </summary>
		private void SaveDataOnce()
		{
			if (this._dataSaved) return;
			this._dataSaved = true;

			try
			{
				CounterDataStore.Save(
					this.viewModel?.Counters,
					this.viewModel?.SortieHistory,
					this.viewModel?.IsCounterEnabled ?? true,
					this.viewModel?.IsSortieHistoryEnabled ?? true,
					this.viewModel?.ShowAirSuperiority ?? true,
					this.viewModel?.IsTopMost ?? true);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Counter] 保存エラー: {ex.Message}");
			}
		}

		public void RequestNotify(string type, string header, string body)
		{
			this.NotifyRequested?.Invoke(this, new NotifyEventArgs(type, header, body));
		}
	}
}
