using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Grabacr07.KanColleViewer.Models.CoreAudio;
using CommunityToolkit.Mvvm.ComponentModel;
using Vannatech.CoreAudio.Constants;
using Vannatech.CoreAudio.Enumerations;
using Vannatech.CoreAudio.Externals;
using Vannatech.CoreAudio.Interfaces;

namespace Grabacr07.KanColleViewer.Models
{
	public class Volume : ObservableObject, IAudioSessionEvents
	{
		private ISimpleAudioVolume? simpleAudioVolume;
		private IAudioSessionControl? sessionControl;

		#region IsMute 変更通知プロパティ

		public bool IsMute
		{
			get { return field; }
			private set
			{
				if (field != value)
				{
					field = value;
					this.OnPropertyChanged(string.Empty);
				}
			}
		}

		#endregion

		public static Volume GetInstance()
		{
			var volume = new Volume();

			var deviceEnumeratorType = Type.GetTypeFromCLSID(new Guid(ComCLSIDs.MMDeviceEnumeratorCLSID))
				?? throw new InvalidOperationException("MMDeviceEnumerator の型を取得できません。");
			var devenum = (IMMDeviceEnumerator)(Activator.CreateInstance(deviceEnumeratorType)
				?? throw new InvalidOperationException("MMDeviceEnumerator を生成できません。"));

			IMMDevice device;
			devenum.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device).ThrowIfError();

			object objSessionManager;
			device.Activate(new Guid(ComIIDs.IAudioSessionManager2IID), (uint)CLSCTX.CLSCTX_INPROC_SERVER, IntPtr.Zero, out objSessionManager).ThrowIfError();
			var sessionManager = objSessionManager as IAudioSessionManager2;
			if (sessionManager == null) throw new Exception("Session is not found.");

			IAudioSessionEnumerator sessions;
			sessionManager.GetSessionEnumerator(out sessions).ThrowIfError();

			// sessionID は空にするとデフォルトセッションが取れるらしい
			ISimpleAudioVolume simpleAudioVolume;
			sessionManager.GetSimpleAudioVolume(Guid.Empty, 0, out simpleAudioVolume).ThrowIfError();
			volume.simpleAudioVolume = simpleAudioVolume;

			bool isMute;
			simpleAudioVolume.GetMute(out isMute).ThrowIfError();
			volume.IsMute = isMute;

			// sessionControl のインスタンスは取っておかないと通知来なくなる
			sessionManager.GetAudioSessionControl(Guid.Empty, 0, out volume.sessionControl).ThrowIfError();
			volume.sessionControl.RegisterAudioSessionNotification(volume).ThrowIfError();

			return volume;
		}

		public void ToggleMute()
		{
			if (this.simpleAudioVolume is not { } simpleAudioVolume) return;

			var newValue = !this.IsMute;
			simpleAudioVolume.SetMute(newValue, Guid.NewGuid()).ThrowIfError();

			bool resultValue;
			simpleAudioVolume.GetMute(out resultValue).ThrowIfError();

			this.IsMute = resultValue;
		}

		#region IAudioSessionEvents members

		int IAudioSessionEvents.OnDisplayNameChanged(string displayName, ref Guid eventContext)
		{
			return 0;
		}

		int IAudioSessionEvents.OnIconPathChanged(string iconPath, ref Guid eventContext)
		{
			return 0;
		}

		int IAudioSessionEvents.OnSimpleVolumeChanged(float volume, bool isMuted, ref Guid eventContext)
		{
			this.IsMute = isMuted;
			return 0;
		}

		int IAudioSessionEvents.OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex, ref Guid eventContext)
		{
			return 0;
		}

		int IAudioSessionEvents.OnGroupingParamChanged(ref Guid groupingId, ref Guid eventContext)
		{
			return 0;
		}

		int IAudioSessionEvents.OnStateChanged(AudioSessionState state)
		{
			return 0;
		}

		int IAudioSessionEvents.OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
		{
			return 0;
		}

		#endregion
	}
}

namespace Grabacr07.KanColleViewer.Models.CoreAudio
{
	internal static class HResultExtensions
	{
		extension(int hResult)
		{
			/// <summary>
			/// HRESULT 値が S_OK (0) 以外の場合、<see cref="COMException"/> をスローします。
			/// </summary>
			public void ThrowIfError(string message = "Session is not found.")
			{
				if (hResult != 0) throw new COMException(message, hResult);
			}
		}
	}
}
