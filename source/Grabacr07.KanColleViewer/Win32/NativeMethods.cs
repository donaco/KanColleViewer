using System;
using System.Runtime.InteropServices;

namespace Grabacr07.KanColleViewer.Win32
{
	internal static class NativeMethods
	{
		[DllImport("Avrt.dll")]
		public static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

		public const int WM_WINDOWPOSCHANGED = 0x0047;
		public const uint SWP_NOSIZE = 0x0001;
		public const uint SWP_NOMOVE = 0x0002;
		public const uint SWP_NOACTIVATE = 0x0010;
		public const uint SWP_NOOWNERZORDER = 0x0200;
		public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
		public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

		public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
	}
}
