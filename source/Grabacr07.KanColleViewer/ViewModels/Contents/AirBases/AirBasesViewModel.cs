using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleWrapper.Models;
using Livet;
using Livet.EventListeners;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Mvvm;
using StatefulModel;

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

		private MultipleDisposable airBaseListeners;

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
			this.airBaseListeners = new MultipleDisposable();

			try
			{
				var client = KanColleClient.Current;
				if (client == null)
				{
					System.Diagnostics.Debug.WriteLine("KanColleClient.Current is null in AirBasesViewModel constructor.");
					return;
				}

				// Homeport がまだ作られていない可能性があるため待機する対応。
				// Homeport が既に存在するなら接続して初期化。
				if (client.Homeport == null)
				{
					System.Diagnostics.Debug.WriteLine("Homeport is null; subscribing to KanColleClient.Homeport changes.");
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
						catch (Exception ex)
						{
							System.Diagnostics.Debug.WriteLine($"AirBasesViewModel: AttachToHomeport failed: {ex}");
						}
					}).AddTo(this);
				}
				else
				{
					AttachToHomeport();
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error in AirBasesTabViewModel constructor: {ex}");
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
				this.airBaseListeners = new MultipleDisposable();

				// AreaGroup プロパティの変更を監視（元の Subscribe に加え、明示的な PropertyChangedEventListener を追加）
				homeport.AirBases
					.Subscribe(nameof(Grabacr07.KanColleWrapper.Models.AirBases.AreaGroup), this.InitializeAirBases)
					.AddTo(this);

				// 直接 PropertyChangedEventListener でも監視（Subscribe が効かない環境へのフォールバック対策）
				var listener = new Livet.EventListeners.PropertyChangedEventListener(homeport.AirBases)
		{
			{ nameof(Grabacr07.KanColleWrapper.Models.AirBases.AreaGroup), (s, e) => this.InitializeAirBases() },
		};
				this.CompositeDisposable.Add(listener);

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
			this.airBaseListeners = new MultipleDisposable();

			try
			{
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
					.OrderBy(x => x.AreaId)
					.ToArray();

				this.IsEmpty = this.AirBases.Length == 0;

				// 先頭を選択
				this.SelectedAirBase = this.AirBases.FirstOrDefault();

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

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				this.airBaseListeners?.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
