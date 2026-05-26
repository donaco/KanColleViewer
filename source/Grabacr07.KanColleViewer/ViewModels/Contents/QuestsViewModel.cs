using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Grabacr07.KanColleViewer.Properties;
using Grabacr07.KanColleViewer.Views;
using Grabacr07.KanColleWrapper;
using Grabacr07.KanColleViewer.Infrastructure.Lifetime;

namespace Grabacr07.KanColleViewer.ViewModels.Contents
{
	public class QuestsViewModel : TabItemViewModel
	{
		// 任務一覧ウィンドウのインスタンスを保持
		private static Window questWindowInstance;

		public override string Name
		{
			get { return Resources.Quests; }
			protected set { throw new NotImplementedException(); }
		}

		#region Current 変更通知プロパティ

		private QuestViewModel[] _Current;

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


		public QuestsViewModel()
		{
			var quests = KanColleClient.Current.Homeport.Quests;

			this.IsUntaken = quests.IsUntaken;
			this.Quests = quests.All.Select(x => new QuestViewModel(x)).ToArray();
			this.Current = quests.Current.Select(x => new QuestViewModel(x)).ToArray();
			this.IsEmpty = quests.IsEmpty;

			System.ComponentModel.PropertyChangedEventHandler questsHandler = (s, e) =>
			{
				if (e.PropertyName == nameof(quests.IsUntaken)) this.IsUntaken = quests.IsUntaken;
				else if (e.PropertyName == nameof(quests.All)) this.Quests = quests.All.Select(x => new QuestViewModel(x)).ToArray();
				else if (e.PropertyName == nameof(quests.Current)) this.Current = quests.Current.Select(x => new QuestViewModel(x)).ToArray();
				else if (e.PropertyName == nameof(quests.IsEmpty)) this.IsEmpty = quests.IsEmpty;
			};
			quests.PropertyChanged += questsHandler;
			this.CompositeDisposable.Add(new DelegateDisposable(() => quests.PropertyChanged -= questsHandler));
		}

		#region 任務一覧ウィンドウを安全に表示
		/// <summary>
		/// null チェックと例外処理を追加して、任務一覧ウィンドウを安全に表示。
		/// 既に開いている場合はアクティブにする。
		/// </summary>
		public void ShowQuestWindow()
		{
			try
			{
				// 既存のウィンドウがあり、閉じられていない場合はアクティブにする
				if (questWindowInstance != null && questWindowInstance.IsLoaded)
				{
					questWindowInstance.Activate();
					if (questWindowInstance.WindowState == WindowState.Minimized)
					{
						questWindowInstance.WindowState = WindowState.Normal;
					}
					return;
				}

				// 新しいウィンドウを作成
				var vm = new QuestWindowViewModel();
				var window = new QuestWindow { DataContext = vm };

				// ウィンドウが閉じられたらインスタンスをクリア
				window.Closed += (s, e) => questWindowInstance = null;

				questWindowInstance = window;
				window.Show();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error in ShowQuestWindow: {ex}");
			}
		}
		#endregion
	}
}
