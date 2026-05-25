using System.Diagnostics;
using CefSharp.RenderProcess;

namespace Grabacr07.KanColleViewer.BrowserSubprocess
{
	internal static class Program
	{
		[System.STAThread]
		private static int Main(string[] args)
		{
			Debug.WriteLine("KanColleViewer.BrowserSubprocess starting up with command line: " + string.Join("\n", args));

			IRenderProcessHandler handler = null;
			var browserProcessExe = new WcfBrowserSubprocessExecutable();
			var result = browserProcessExe.Main(args, handler);

			Debug.WriteLine("KanColleViewer.BrowserSubprocess shutting down.");
			return result;
		}
	}
}
