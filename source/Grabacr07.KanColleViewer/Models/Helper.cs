using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Grabacr07.KanColleViewer.Models.Cef;
using Grabacr07.KanColleViewer.Models.Settings;
using Grabacr07.KanColleViewer.Win32;

namespace Grabacr07.KanColleViewer.Models
{
	internal static class Helper
	{
		static Helper()
		{
			var version = Environment.OSVersion.Version;
			IsWindows8OrGreater = (version.Major == 6 && version.Minor >= 2) || version.Major > 6;
		}


		/// <summary>
		/// Windows 8 またはそれ以降のバージョンで動作しているかどうかを確認します。
		/// </summary>
		public static bool IsWindows8OrGreater { get; private set; }

		/// <summary>
		/// デザイナーのコンテキストで実行されているかどうかを取得します。
		/// </summary>
		public static bool IsInDesignMode => DesignerProperties.GetIsInDesignMode(new DependencyObject());

		/// <summary>
		/// スクリーンショットの保存先として、保存先フォルダの配下にあるファイルパスを生成します。
		/// </summary>

		public static string CreateScreenshotFilePath(SupportedImageFormat format)
		{
			var destination = Path.GetFullPath(ScreenshotSettings.Destination);
			var filePath = Path.GetFullPath(Path.Combine(
				destination,
				$"KanColle-{DateTimeOffset.Now.LocalDateTime:yyMMdd-HHmmssff}"));

			// 生成したパスが保存先フォルダの配下にあることを確認
			if (!filePath.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("スクリーンショットの保存先が不正です。");
			}

			return Path.ChangeExtension(filePath, format.ToExtension());
		}

		public static void SetMMCSSTask()
		{
			var index = 0u;
			NativeMethods.AvSetMmThreadCharacteristics("Games", ref index);
		}

		public static void DeleteCacheIfRequested()
		{
			if (GeneralSettings.ClearCacheOnNextStartup)
			{
				if (System.Diagnostics.Debugger.IsAttached)
				{
					System.Diagnostics.Debug.WriteLine("DeleteCacheIfRequested skipped during debugging.");
					return;
				}

				try
				{
					Directory.Delete(CefBridge.CachePath, true);
					GeneralSettings.ClearCacheOnNextStartup.Value = false;
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine(ex);
				}
			}
		}


		private static byte ParseHexByte(ReadOnlySpan<char> hex)
			=> byte.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

		public static Color StringToColor(string colorCode)
		{
			try
			{
				if (colorCode.StartsWith("#"))
				{
					if (colorCode.Length == 7)
					{
						// #rrggbb style
						return Color.FromRgb(
							ParseHexByte(colorCode.AsSpan(1, 2)),
							ParseHexByte(colorCode.AsSpan(3, 2)),
							ParseHexByte(colorCode.AsSpan(5, 2)));
					}
					if (colorCode.Length == 9)
					{
						// #aarrggbb style
						return Color.FromArgb(
							ParseHexByte(colorCode.AsSpan(1, 2)),
							ParseHexByte(colorCode.AsSpan(3, 2)),
							ParseHexByte(colorCode.AsSpan(5, 2)),
							ParseHexByte(colorCode.AsSpan(7, 2)));
					}
				}
			}
			catch (Exception ex)
			{
				// 雑
				System.Diagnostics.Debug.WriteLine(ex);
			}

			return Colors.Transparent;
		}

		public static HttpClientHandler GetProxyConfiguredHandler()
		{
			switch (Settings.NetworkSettings.Proxy.Type.Value)
			{
				case Grabacr07.KanColleWrapper.ProxyType.DirectAccess:
					return new HttpClientHandler
					{
						UseProxy = false,
					};

				case Grabacr07.KanColleWrapper.ProxyType.SpecificProxy:
					return new HttpClientHandler
					{
						UseProxy = true,
						Proxy = new WebProxy($"{Settings.NetworkSettings.Proxy.Host.Value}:{Settings.NetworkSettings.Proxy.Port.Value}"),
					};

				case Grabacr07.KanColleWrapper.ProxyType.SystemProxy:
					return new HttpClientHandler();

				default:
					return new HttpClientHandler();
			}
		}
	}
}
