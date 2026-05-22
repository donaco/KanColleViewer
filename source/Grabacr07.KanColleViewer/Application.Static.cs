using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
		}

		public static Application Instance => Current as Application;

		internal static void ReportRecoverableException(string caller, object sender, Exception exception)
		{
			WriteExceptionReport(caller, sender, exception);
		}

		private static void ReportException(string caller, object sender, Exception exception)
		{
			WriteExceptionReport(caller, sender, exception);
			Current?.Shutdown();
		}

		private static void WriteExceptionReport(string caller, object sender, Exception exception)
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

				var cefLogFilePath = CefBridge.LogFilePath;
				var message = $@"*** Error Report ({caller}) ***
					{ProductInfo.Product} ver.{ProductInfo.VersionString}
					{now}

					{new SystemEnvironment()}

					Sender: {(sender is Type t ? t : sender?.GetType())?.FullName}
					Exception: {exception?.GetType().FullName}
					Cef.IsInitialized: {Cef.IsInitialized}
					Cef.LogFile: {cefLogFilePath}
					Cef.LogFile.Exists: {File.Exists(cefLogFilePath)}
					Cef.CachePath: {CefBridge.CachePath}

					{BuildExceptionDetails(exception)}
					";
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.AppendAllText(path, message);
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}
		}

		private static string BuildExceptionDetails(Exception exception)
		{
			if (exception == null)
			{
				return "<no exception information>";
			}

			var builder = new StringBuilder();
			var depth = 0;
			for (var current = exception; current != null; current = current.InnerException)
			{
				builder.AppendLine($"[Level {depth}] {current.GetType().FullName}");
				builder.AppendLine(current.Message);
				builder.AppendLine(current.ToString());
				builder.AppendLine();
				depth++;
			}

			return builder.ToString();
		}
	}
}
