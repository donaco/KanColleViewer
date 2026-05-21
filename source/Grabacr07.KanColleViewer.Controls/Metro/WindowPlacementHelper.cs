using System;
using System.Windows;
using Microsoft.Win32;

namespace Grabacr07.KanColleViewer.Controls.Metro
{
    /// <summary>
    /// ウィンドウの位置・サイズを ApplicationSettings に保存・復元するヘルパーです。
    /// Phase 4: MetroRadiance の WindowSettings / IWindowSettings の代替実装です。
    /// </summary>
    internal static class WindowPlacementHelper
    {
        private const string KeyPrefix = "WindowPlacement_";

        public static void Save(Window window)
        {
            try
            {
                var key = KeyPrefix + window.GetType().FullName;
                var value = $"{window.Left},{window.Top},{window.Width},{window.Height},{(int)window.WindowState}";
                Registry.CurrentUser.CreateSubKey(@"Software\KanColleViewer\WindowPlacements")
                    ?.SetValue(key, value);
            }
            catch { /* 保存失敗は無視 */ }
        }

        public static void Restore(Window window)
        {
            try
            {
                var key = KeyPrefix + window.GetType().FullName;
                var regKey = Registry.CurrentUser.OpenSubKey(@"Software\KanColleViewer\WindowPlacements");
                var regValue = regKey?.GetValue(key) as string;
                if (regValue == null) return;
                var value = regValue;

                var parts = value.Split(',');
                if (parts.Length < 5) return;

                if (double.TryParse(parts[0], out var left)   &&
                    double.TryParse(parts[1], out var top)    &&
                    double.TryParse(parts[2], out var width)  &&
                    double.TryParse(parts[3], out var height) &&
                    int.TryParse(parts[4], out var state))
                {
                    window.Left   = left;
                    window.Top    = top;
                    window.Width  = width;
                    window.Height = height;

                    // Minimized 状態は Normal で復元
                    var ws = (WindowState)state;
                    window.WindowState = ws == WindowState.Minimized ? WindowState.Normal : ws;
                }
            }
            catch { /* 復元失敗は無視 */ }
        }
    }
}
