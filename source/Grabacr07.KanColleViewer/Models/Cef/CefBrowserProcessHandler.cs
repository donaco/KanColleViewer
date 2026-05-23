using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CefSharp;

namespace Grabacr07.KanColleViewer.Models.Cef
{
	internal sealed class CefBrowserProcessHandler : IBrowserProcessHandler, IDisposable
	{
		private static readonly object logLock = new object();
		private static readonly string logFilePath = Path.Combine(CefBridge.CachePath, "browserprocess.log");

		public void OnContextInitialized()
		{
			WriteLog("OnContextInitialized", Environment.CommandLine);
		}

		public void OnScheduleMessagePumpWork(long delayMs)
		{
		}

		public bool OnAlreadyRunningAppRelaunch(IReadOnlyDictionary<string, string> commandLineArgs, string currentWorkingDirectory)
		{
			var args = commandLineArgs == null
				? "<null>"
				: string.Join(" | ", commandLineArgs.Select(x => $"{x.Key}={x.Value}"));
			WriteLog("OnAlreadyRunningAppRelaunch", $"cwd={currentWorkingDirectory}\r\n{args}");
			return false;
		}

		// IBrowserProcessHandler の必須メンバ（CefSharp 145 互換性対応）
		public void OnBeforeChildProcessLaunch(string commandLine)
		{
		}

		public void OnRenderProcessThreadCreated(CefThreadIds threadId)
		{
		}

		public void Dispose()
		{
		}

		private static void WriteLog(string title, string detail)
		{
			try
			{
				lock (logLock)
				{
					Directory.CreateDirectory(CefBridge.CachePath);
					File.AppendAllText(logFilePath, $"[{DateTimeOffset.Now:O}] {title}\r\n{detail}\r\n\r\n");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}
		}
	}
}
