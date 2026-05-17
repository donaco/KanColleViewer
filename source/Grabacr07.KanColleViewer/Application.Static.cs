using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CefSharp;
using CefSharp.Wpf;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Models.Cef;
using Grabacr07.KanColleWrapper;
using MetroTrilithon.Desktop;

namespace Grabacr07.KanColleViewer
{
	partial class Application
	{
		static Application()
		{
			AppDomain.CurrentDomain.UnhandledException += (sender, args) => ReportException("AppDomain", sender, args.ExceptionObject as Exception);
			AppDomain.CurrentDomain.AssemblyResolve += CefBridge.ResolveCefSharpAssembly;
		}

		public static Application Instance => Current as Application;

		private static void ReportException(string caller, object sender, Exception exception)
		{
			try
			{
				var now = DateTimeOffset.Now;
				var path = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					ProductInfo.Company,
					ProductInfo.Product,
					"ErrorReports",
					$"ErrorReport-{now:yyyyMMdd-HHmmss}-{now.Millisecond:000}.log");

				var message = $@"*** Error Report ({caller}) ***
					{ProductInfo.Product} ver.{ProductInfo.VersionString}
					{now}

					{new SystemEnvironment()}

					Sender:    {(sender is Type t ? t : sender?.GetType())?.FullName}
					Exception: {exception?.GetType().FullName}

					{exception}
					";
				// ReSharper disable once AssignNullToNotNullAttribute
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.AppendAllText(path, message);
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}

			Current.Shutdown();
		}
	}
}
