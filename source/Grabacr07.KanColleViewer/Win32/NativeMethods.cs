using System;
using System.Runtime.InteropServices;

namespace Grabacr07.KanColleViewer.Win32
{
	internal static class NativeMethods
	{
		[DllImport("Avrt.dll")]
		public static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

		/// <summary>プライマリモニターのリフレッシュレート取得に使用する定数 (VREFRESH)</summary>
		public const int VREFRESH = 116;

		[DllImport("user32.dll")]
		public static extern IntPtr GetDC(IntPtr hWnd);

		[DllImport("user32.dll")]
		public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

		[DllImport("gdi32.dll")]
		public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
	}
}
