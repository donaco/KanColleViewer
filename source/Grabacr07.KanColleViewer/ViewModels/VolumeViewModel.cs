using System;
using System.IO;
using CefSharp.Wpf;
using Livet;

namespace Grabacr07.KanColleViewer.ViewModels
{
	public class VolumeViewModel : ViewModel
	{
		#region IsMute 変更通知プロパティ

		private bool _IsMute;

		public bool IsMute
		{
			get { return this._IsMute; }
			set
			{
				if (this._IsMute != value)
				{
					this._IsMute = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion


		public VolumeViewModel() { }

		public void ToggleMute()
		{
			var newMute = !this.IsMute;
			var browser = WindowService.Current.FindBrowser();
			if (browser != null)
			{
				try
				{
					browser.GetBrowser()?.GetHost()?.SetAudioMuted(newMute);
					this.IsMute = newMute;
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[VolumeViewModel.ToggleMute] {ex.Message}");
				}
			}
			else
			{
				// ブラウザ未初期化でも UI 状態だけ更新しておく
				this.IsMute = newMute;
			}
		}
	}
}
