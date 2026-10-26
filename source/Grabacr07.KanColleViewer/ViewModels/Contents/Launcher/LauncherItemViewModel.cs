using System;
using Grabacr07.KanColleViewer.Infrastructure.Mvvm;
using MetroTrilithon.Serialization;

namespace Grabacr07.KanColleViewer.ViewModels.Contents.Launcher
{
	/// <summary>
	/// ウィンドウランチャーの 1 項目を表す ViewModel です。
	/// </summary>
	public class LauncherItemViewModel : ViewModelBase
	{
		private readonly Func<string> getName;
		private readonly SerializableProperty<bool> setting;
		private readonly Func<bool> canOpen;
		private readonly Action open;
		private readonly Action close;

		/// <summary>
		/// 表示名を取得します。
		/// </summary>
		public string Name => this.getName();

		/// <summary>
		/// 一括操作の対象にするかどうか (設定ファイルに保存) を取得または設定します。
		/// </summary>
		public bool IsChecked
		{
			get { return this.setting.Value; }
			set
			{
				if (this.setting.Value != value)
				{
					this.setting.Value = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#region IsAvailable 変更通知プロパティ

		private bool _IsAvailable;

		/// <summary>
		/// 情報が取得できており、ウィンドウを表示できるかどうかを取得します。
		/// </summary>
		public bool IsAvailable
		{
			get { return this._IsAvailable; }
			private set
			{
				if (this._IsAvailable != value)
				{
					this._IsAvailable = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public LauncherItemViewModel(Func<string> getName, SerializableProperty<bool> setting, Func<bool> canOpen, Action open, Action close)
		{
			this.getName = getName;
			this.setting = setting;
			this.canOpen = canOpen;
			this.open = open;
			this.close = close;
		}

		/// <summary>
		/// 表示可否と表示名を最新の状態に更新します。
		/// </summary>
		public void Refresh()
		{
			try
			{
				this.IsAvailable = this.canOpen();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Launcher] Refresh failed: {ex.Message}");
				this.IsAvailable = false;
			}
			this.RaisePropertyChanged(nameof(this.Name));
		}

		/// <summary>
		/// 表示可能な場合のみウィンドウを開きます。
		/// </summary>
		public void Open()
		{
			this.Refresh();
			if (!this.IsAvailable) return;

			try
			{
				this.open();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Launcher] Open failed: {ex.Message}");
			}
		}

		/// <summary>
		/// ウィンドウを閉じます。
		/// </summary>
		public void Close()
		{
			try
			{
				this.close();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Launcher] Close failed: {ex.Message}");
			}
		}
	}
}
