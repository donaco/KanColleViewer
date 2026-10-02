using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Shell;
using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleWrapper;
using CommunityToolkit.Mvvm.ComponentModel;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.Models
{
	public class TaskbarProgress : ObservableObject, ITaskbarProgress, IDisposable
	{
		public static ITaskbarProgress[] Features => PluginService.Current.Get<ITaskbarProgress>();

		private readonly CompositeDisposable compositDisposable = new CompositeDisposable();
		private ITaskbarProgress? current;

		string ITaskbarProgress.Id
		{
			get { throw new NotSupportedException(); }
		}

		string ITaskbarProgress.DisplayName
		{
			get { throw new NotSupportedException(); }
		}

		#region State 変更通知プロパティ

		private TaskbarItemProgressState _State;

		public TaskbarItemProgressState State
		{
			get { return this._State; }
			set
			{
				if (this._State != value)
				{
					this._State = value;
					this.OnPropertyChanged(string.Empty);
				}
			}
		}

		#endregion

		#region Value 変更通知プロパティ

		public double Value
		{
			get { return field; }
			set
			{
				if (!field.Equals(value))
				{
					field = value;
					this.OnPropertyChanged(string.Empty);
				}
			}
		}

		#endregion

		public event EventHandler? Updated;

		public TaskbarProgress()
		{
			GeneralSettings.TaskbarProgressSource
				.Subscribe(x => this.Change(normalSource: x))
				.AddTo(this.compositDisposable);

			GeneralSettings.TaskbarProgressSourceWhenSortie
				.Subscribe(x => this.Change(sortieSource: x))
				.AddTo(this.compositDisposable);

			KanColleClient.Current
				.Subscribe(nameof(KanColleClient.IsInSortie), () => this.Change())
				.AddTo(this.compositDisposable);
		}

		public void Change(string? normalSource = null, string? sortieSource = null)
		{
			var id = KanColleClient.Current.IsInSortie
				? (sortieSource ?? GeneralSettings.TaskbarProgressSourceWhenSortie)
				: (normalSource ?? GeneralSettings.TaskbarProgressSource);
			var progress = Features.FirstOrDefault(x => x.Id == id);

			this.Change(progress);
		}

		public void Change(ITaskbarProgress? progress)
		{
			if (this.current != null)
			{
				this.current.Updated -= this.CurrentOnUpdated;
			}

			if (progress == null)
			{
				this.current = null;
				this.State = TaskbarItemProgressState.None;
				this.Value = .0;
			}
			else
			{
				this.current = progress;
				this.current.Updated += this.CurrentOnUpdated;

				this.State = progress.State;
				this.Value = progress.Value;
			}

			this.Updated?.Invoke(this, EventArgs.Empty);
			this.OnPropertyChanged(nameof(this.Updated));
		}

		private void CurrentOnUpdated(object? sender, EventArgs eventArgs)
		{
			if (sender is not ITaskbarProgress progress) return;

			this.State = progress.State;
			this.Value = progress.Value;

			this.Updated?.Invoke(this, EventArgs.Empty);
			this.OnPropertyChanged(nameof(this.Updated));
		}

		public void Dispose() => this.compositDisposable.Dispose();
	}
}
