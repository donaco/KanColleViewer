using System;
using Grabacr07.KanColleViewer.Win32;

namespace Grabacr07.KanColleViewer.Models
{
	/// <summary>
	/// モニター情報を取得するユーティリティです。
	/// </summary>
	internal static class MonitorHelper
	{
		private static int? _cachedRefreshRate;

		/// <summary>
		/// プライマリモニターのリフレッシュレートを取得します。
		/// 取得できない場合は 60 を返します。
		/// 値はアプリ起動時に一度だけ取得してキャッシュされます。
		/// </summary>
		public static int PrimaryRefreshRate
		{
			get
			{
				if (!_cachedRefreshRate.HasValue)
					_cachedRefreshRate = FetchRefreshRate();
				return _cachedRefreshRate.Value;
			}
		}

		/// <summary>
		/// High モードの ComboBox 表示文字列を返します（検出した FPS を含む）。
		/// </summary>
		public static string HighModeDisplayText
			=> string.Format("高：{0}FPS　モニタの FPS に自動調整", PrimaryRefreshRate);

		private static int FetchRefreshRate()
		{
			var hdc = IntPtr.Zero;
			try
			{
				hdc = NativeMethods.GetDC(IntPtr.Zero); // プライマリモニターの DC を取得
				if (hdc == IntPtr.Zero) return 60;

				var rate = NativeMethods.GetDeviceCaps(hdc, NativeMethods.VREFRESH);
				return rate > 0 ? rate : 60;
			}
			catch
			{
				return 60; // 取得失敗時はフォールバック
			}
			finally
			{
				if (hdc != IntPtr.Zero)
					NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
			}
		}
	}
}
