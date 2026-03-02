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
	[ExportMetadata("Description", "シンプルな回数カウント機能、出撃履歴を提供します。")]
	[ExportMetadata("Version", "2.0.0")]
	[ExportMetadata("Author", "@Grabacr07/@Donaco")]
	public class KanColleCounter : IPlugin, ITool, IRequestNotify
	{
		private CounterViewModel viewModel;

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
					// 出撃履歴（直近10件を表示）
					SortieHistory = new SortieHistoryCounter(proxy, 10),
				};

				// アプリケーション終了時にポップアップウィンドウを閉じる
				if (System.Windows.Application.Current != null)
				{
					System.Windows.Application.Current.Exit += (s, e) =>
					{
						this.viewModel?.ClosePopupWindow();
					};
				}
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

		public void RequestNotify(string type, string header, string body)
		{
			this.NotifyRequested?.Invoke(this, new NotifyEventArgs(type, header, body));
		}
	}
}
