using System;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace Grabacr07.KanColleViewer.Composition
{
	/// <summary>
	/// プラグインを改修せずに、リフレクションでプラグインの別ウィンドウを操作するためのヘルパーです。
	/// 失敗した場合は例外を投げず、null / false を返します。
	/// </summary>
	internal static class PluginWindowInvoker
	{
		private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		/// <summary>
		/// [ツール] タブ名 (<see cref="ITool.Name"/>) が一致するプラグインを取得します。
		/// </summary>
		public static ITool? FindTool(string toolName)
		{
			try
			{
				return PluginService.Current.Get<ITool>().FirstOrDefault(x => x.Name == toolName);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[PluginWindowInvoker] FindTool({toolName}) failed: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// 指定したオブジェクトのフィールド値を取得します (private も可)。
		/// </summary>
		public static object? GetField(object? target, string fieldName)
		{
			if (target == null) return null;
			try
			{
				return target.GetType().GetField(fieldName, MemberFlags)?.GetValue(target);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[PluginWindowInvoker] GetField({fieldName}) failed: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// 指定したオブジェクトのプロパティ値を取得します。
		/// </summary>
		public static object? GetProperty(object? target, string propertyName)
		{
			if (target == null) return null;
			try
			{
				return target.GetType().GetProperty(propertyName, MemberFlags)?.GetValue(target);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[PluginWindowInvoker] GetProperty({propertyName}) failed: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// 引数なしのメソッドが存在するかどうかを返します。
		/// </summary>
		public static bool HasMethod(object? target, string methodName)
		{
			if (target == null) return false;
			try
			{
				return target.GetType().GetMethod(methodName, MemberFlags, Type.EmptyTypes) != null;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// 引数なしのメソッドを呼び出します。成功した場合は true を返します。
		/// </summary>
		public static bool InvokeMethod(object? target, string methodName)
		{
			if (target == null) return false;
			try
			{
				var method = target.GetType().GetMethod(methodName, MemberFlags, Type.EmptyTypes);
				if (method == null) return false;
				method.Invoke(target, null);
				return true;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[PluginWindowInvoker] InvokeMethod({methodName}) failed: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// DataContext と型名が一致する、開いているウィンドウを取得します。
		/// </summary>
		public static Window? FindWindow(object? dataContext, string windowTypeName)
		{
			if (dataContext == null) return null;
			try
			{
				return System.Windows.Application.Current?.Windows
					.OfType<Window>()
					.FirstOrDefault(w => w.IsLoaded
						&& ReferenceEquals(w.DataContext, dataContext)
						&& w.GetType().Name == windowTypeName);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[PluginWindowInvoker] FindWindow({windowTypeName}) failed: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// DataContext と型名が一致する、開いているウィンドウをすべて閉じます。
		/// </summary>
		public static void CloseWindows(object? dataContext, string windowTypeName)
		{
			if (dataContext == null) return;
			try
			{
				var windows = System.Windows.Application.Current?.Windows
					.OfType<Window>()
					.Where(w => w.IsLoaded
						&& ReferenceEquals(w.DataContext, dataContext)
						&& w.GetType().Name == windowTypeName)
					.ToArray() ?? Array.Empty<Window>();

				foreach (var window in windows)
				{
					window.Close();
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[PluginWindowInvoker] CloseWindows({windowTypeName}) failed: {ex.Message}");
			}
		}
	}
}
