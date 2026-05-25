using System;
using System.Collections.Generic;
using System.Diagnostics;

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
			// ErrorReport 出力を停止
		}
	}
}
