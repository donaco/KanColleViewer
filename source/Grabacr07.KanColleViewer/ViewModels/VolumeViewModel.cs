using System;
using Grabacr07.KanColleViewer.Models.Settings;
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


		public VolumeViewModel()
		{
			this.IsMute = GeneralSettings.IsMuted.Value;
		}

		public void ToggleMute()
		{
			var newMute = !this.IsMute;
			GeneralSettings.IsMuted.Value = newMute;

			var browser = WindowService.Current.FindBrowser();
			if (browser != null)
			{
				try
				{
					browser.GetBrowser()?.GetHost()?.SetAudioMuted(newMute);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[VolumeViewModel.ToggleMute] {ex.Message}");
				}
			}

			this.IsMute = newMute;
		}
	}
}
