using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.AirBases
{
	/// <summary>
	/// 航空隊タブの ViewModel
	/// </summary>
	public class AirBasesViewModel : TabItemViewModel
	{

		#region IsEmpty 情報未取得時の通知表示

		private bool _IsEmpty;

		public bool IsEmpty
		{
			get { return this._IsEmpty; }
			set
			{
				if (this._IsEmpty != value)
				{
					this._IsEmpty = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		private CompositeDisposable airBaseListeners;

		public override string Name
		{
			get { return "航空隊"; }
			protected set { }
		}

		#region AirBases

		private AirBaseViewModel[] _AirBases;

		public AirBaseViewModel[] AirBases
		{
			get { return this._AirBases; }
			set
			{
				if (this._AirBases != value)
				{
					this._AirBases = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region SelectedAirBase

		private AirBaseViewModel _SelectedAirBase;

		public AirBaseViewModel SelectedAirBase
		{
			get { return this._SelectedAirBase; }
			set
			{
				if (this._SelectedAirBase != value)
				{
					this._SelectedAirBase = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public AirBasesViewModel()
		{
			this.airBaseListeners = new CompositeDisposable();

			try
			{
				var client = KanColleClient.Current;
				if (client == null)
				{
					return;
				}

				// Homeport がまだ作られていない可能性があるため待機する対応。
				// Homeport が既に存在するなら接続して初期化。
				if (client.Homeport == null)
				{
					// Homeport プロパティがセットされたら Attach を試みる
					client.Subscribe(nameof(KanColleClient.Homeport), () =>
					{
						try
						{
							if (KanColleClient.Current?.Homeport != null)
							{
								AttachToHomeport();
							}
						}
						catch
						{
						}
					}).AddTo(this);
				}
				else
				{
					AttachToHomeport();
				}
			}
			catch
			{
			}
		}

		// Homeport に接続して AirBases の変更を監視するヘルパー
		private void AttachToHomeport()
		{
			try
			{
				var homeport = KanColleClient.Current?.Homeport;
				if (homeport == null)
				{
					return;
				}

				if (homeport.AirBases == null)
				{
					return;
				}

				// 既存のリスナを解除してから再接続（Homeport 置換時の二重登録防止）
				this.airBaseListeners?.Dispose();
				this.airBaseListeners = new CompositeDisposable();

				// AreaGroup プロパティの変更を監視
				System.ComponentModel.PropertyChangedEventHandler airBasesHandler = (s, e) =>
				{
					if (e.PropertyName == nameof(Grabacr07.KanColleWrapper.Models.AirBases.AreaGroup)) this.InitializeAirBases();
				};
				homeport.AirBases.PropertyChanged += airBasesHandler;
				this.CompositeDisposable.Add(new DelegateDisposable(() => homeport.AirBases.PropertyChanged -= airBasesHandler));

				// 初期化を試みる
				this.InitializeAirBases();
			}
			catch
			{
			}
		}

		private void InitializeAirBases()
		{
			this.airBaseListeners?.Dispose();
			this.airBaseListeners = new CompositeDisposable();

			try
			{
				// 現在選択中の AreaId を保持
				var currentSelectedAreaId = this.SelectedAirBase?.AreaId;

				var areaGroup = KanColleClient.Current?.Homeport?.AirBases?.AreaGroup;
				if (areaGroup == null)
				{
					this.AirBases = new AirBaseViewModel[0];
					this.SelectedAirBase = null;
					this.IsEmpty = true;
					return;
				}

				this.AirBases = areaGroup
					.Select(kvp => new AirBaseViewModel(kvp.Value))
					.OrderBy(x => GetAreaSortOrder(x.AreaId))
					.ThenBy(x => x.AreaId)
					.ToArray();

				this.IsEmpty = this.AirBases.Length == 0;

				// 以前選択していた AreaId と同じ海域を再選択（なければ先頭）
				if (currentSelectedAreaId.HasValue)
				{
					this.SelectedAirBase = this.AirBases.FirstOrDefault(x => x.AreaId == currentSelectedAreaId.Value)
										?? this.AirBases.FirstOrDefault();
				}
				else
				{
					this.SelectedAirBase = this.AirBases.FirstOrDefault();
				}

				foreach (var ab in this.AirBases)
				{
					this.airBaseListeners.Add(ab);
				}

			}
			catch
			{
				this.AirBases = new AirBaseViewModel[0];
				this.SelectedAirBase = null;
				this.IsEmpty = true;
			}
		}

		/// <summary>
		/// 海域IDから表示順序の優先度を取得（小さいほど先に表示）
		/// </summary>
		private static int GetAreaSortOrder(int areaId)
		{
			// 期間限定海域（areaId >= 60）を最初に表示
			if (areaId >= 60)
			{
				return 0;
			}

			// その他の海域は AreaId の順序で表示
			return 1;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				this.airBaseListeners?.Dispose();
			}

			base.Dispose(disposing);
		}

		// 基地詳細ウィンドウのインスタンスを保持
		private static Window airBaseWindowInstance;

		/// <summary>
		/// 基地詳細ウィンドウを表示する（選択中の基地を渡す）
		/// 既に開いている場合はアクティブにする
		/// </summary>
		public void ShowAirBaseWindow()
		{
			try
			{
				if (this.SelectedAirBase == null)
				{
					return;
				}

				// 既存のウィンドウがあり、閉じられていない場合はアクティブにする
				if (airBaseWindowInstance != null && airBaseWindowInstance.IsLoaded)
				{
					airBaseWindowInstance.Activate();
					if (airBaseWindowInstance.WindowState == WindowState.Minimized)
					{
						airBaseWindowInstance.WindowState = WindowState.Normal;
					}
					return;
				}

				// 新しいウィンドウを作成
				var vm = new Grabacr07.KanColleViewer.ViewModels.AirBaseWindowViewModel(this);
				var window = new Grabacr07.KanColleViewer.Views.AirBaseWindow { DataContext = vm };

				// ウィンドウが閉じられたらインスタンスをクリア
				window.Closed += (s, e) => airBaseWindowInstance = null;

				airBaseWindowInstance = window;
				window.Show();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error in ShowAirBaseWindow: {ex}");
			}
		}
	}
}
