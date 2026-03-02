using System;
using System.Linq;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.ViewModels.Contents;
using Grabacr07.KanColleWrapper;
using Livet.EventListeners;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.ViewModels
{
	public class QuestWindowViewModel : WindowViewModel
	{
		#region Current 変更通知プロパティ

		private QuestViewModel[] _Current;

		/// <summary>
		/// 現在遂行中の任務一覧を取得します。
		/// </summary>
		public QuestViewModel[] Current
		{
			get { return this._Current; }
			set
			{
				if (this._Current != value)
				{
					this._Current = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region Quests 変更通知プロパティ

		private QuestViewModel[] _Quests;

		/// <summary>
		/// 全任務一覧を取得します（デイリー・ウィークリー等のフィルタ用）。
		/// </summary>
		public QuestViewModel[] Quests
		{
			get { return this._Quests; }
			set
			{
				if (this._Quests != value)
				{
					this._Quests = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsUntaken 変更通知プロパティ

		private bool _IsUntaken;

		public bool IsUntaken
		{
			get { return this._IsUntaken; }
			set
			{
				if (this._IsUntaken != value)
				{
					this._IsUntaken = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsEmpty 変更通知プロパティ

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

		/// <summary>
		/// ウィンドウ個別設定 (TopMost 等)。
		/// </summary>
		public QuestWindowSettings Settings { get; }

		public QuestWindowViewModel()
		{
			this.Title = "任務一覧";
			this.Settings = new QuestWindowSettings();

			try
			{
				var quests = KanColleClient.Current?.Homeport?.Quests;
				if (quests == null)
				{
					System.Diagnostics.Debug.WriteLine("Quests is null in QuestWindowViewModel constructor.");
					this.Current = new QuestViewModel[0];
					this.Quests = new QuestViewModel[0];
					return;
				}

				this.IsUntaken = quests.IsUntaken;
				this.IsEmpty = quests.IsEmpty;
				this.Quests = quests.All.Select(x => new QuestViewModel(x)).ToArray();
				this.Current = quests.Current.Select(x => new QuestViewModel(x)).ToArray();

				this.CompositeDisposable.Add(new PropertyChangedEventListener(quests)
				{
					{ nameof(quests.IsUntaken), (sender, args) => this.IsUntaken = quests.IsUntaken },
					{ nameof(quests.IsEmpty), (sender, args) => this.IsEmpty = quests.IsEmpty },
					{ nameof(quests.All), (sender, args) => this.Quests = quests.All.Select(x => new QuestViewModel(x)).ToArray() },
					{ nameof(quests.Current), (sender, args) => this.Current = quests.Current.Select(x => new QuestViewModel(x)).ToArray() },
				});
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error in QuestWindowViewModel constructor: {ex}");
				this.Current = new QuestViewModel[0];
				this.Quests = new QuestViewModel[0];
			}
		}
	}
}
