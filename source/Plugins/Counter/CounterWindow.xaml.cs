using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Counter
{
	/// <summary>
	/// CounterWindow.xaml の相互作用ロジック
	/// </summary>
	public partial class CounterWindow
	{
		public CounterWindow()
		{
			InitializeComponent();
		}

		private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var imageFormat = this.GetScreenshotFormat();
				var path = this.CreateScreenshotFilePath(imageFormat);
				this.CaptureWindow(path, imageFormat?.ToString());
				this.NotifyStatus("Screenshot_Saved", Path.GetFileName(path));
			}
			catch (Exception ex)
			{
				this.NotifyStatus("Screenshot_Failed", ex.Message);
			}
		}

		private void NotifyStatus(string resourceName, string message)
		{
			try
			{
				var statusServiceType = this.GetKanColleViewerType("Grabacr07.KanColleViewer.Models.StatusService");
				var currentProperty = statusServiceType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
				var statusService = currentProperty?.GetValue(null);
				var notifyMethod = statusService?.GetType().GetMethod("Notify", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
				var text = this.GetResourceString(resourceName) + message;
				notifyMethod?.Invoke(statusService, new object[] { text });
			}
			catch
			{
			}
		}

		private string GetResourceString(string resourceName)
		{
			var resourcesType = this.GetKanColleViewerType("Grabacr07.KanColleViewer.Properties.Resources");
			var resourceProperty = resourcesType.GetProperty(resourceName, BindingFlags.Public | BindingFlags.Static);
			return resourceProperty?.GetValue(null) as string ?? string.Empty;
		}

		private object GetScreenshotFormat()
		{
			var settingsType = this.GetKanColleViewerType("Grabacr07.KanColleViewer.Models.Settings.ScreenshotSettings");
			var formatProperty = settingsType?.GetProperty("Format", BindingFlags.Public | BindingFlags.Static);
			var formatSetting = formatProperty?.GetValue(null);
			var valueProperty = formatSetting?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
			return valueProperty?.GetValue(formatSetting) ?? "Png";
		}

		private string CreateScreenshotFilePath(object imageFormat)
		{
			var helperType = this.GetKanColleViewerType("Grabacr07.KanColleViewer.Models.Helper");
			var createPathMethod = helperType?.GetMethod("CreateScreenshotFilePath", BindingFlags.Public | BindingFlags.Static);
			if (createPathMethod != null)
			{
				return (string)createPathMethod.Invoke(null, new[] { imageFormat });
			}

			var destination = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
			var filePath = Path.Combine(destination, $"KanColle-{DateTimeOffset.Now.LocalDateTime:yyMMdd-HHmmssff}");
			var extension = string.Equals(imageFormat?.ToString(), "Jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
			return Path.ChangeExtension(filePath, extension);
		}

		private Type GetKanColleViewerType(string typeName)
		{
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			var assembly = assemblies.FirstOrDefault(x => string.Equals(x.GetName().Name, "KanColleViewer", StringComparison.OrdinalIgnoreCase))
				?? assemblies.FirstOrDefault(x => x.GetType(typeName, false) != null);
			if (assembly == null)
			{
				throw new InvalidOperationException("KanColleViewer 本体アセンブリが見つかりません。");
			}

			var type = assembly.GetType(typeName) ?? assemblies.Select(x => x.GetType(typeName, false)).FirstOrDefault(x => x != null);
			if (type == null)
			{
				throw new InvalidOperationException(typeName + " が見つかりません。");
			}

			return type;
		}

		private void CaptureWindow(string path, string imageFormat)
		{
			var source = PresentationSource.FromVisual(this);
			var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
			var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

			var width = this.ActualWidth;
			var height = this.ActualHeight;
			if (width <= 0 || height <= 0)
			{
				throw new InvalidOperationException("ウィンドウサイズを取得できませんでした。");
			}

			var renderWidth = (int)(width * dpiX);
			var renderHeight = (int)(height * dpiY);

			var renderTarget = new RenderTargetBitmap(renderWidth, renderHeight, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
			renderTarget.Render(this);

			BitmapEncoder encoder = string.Equals(imageFormat, "Jpeg", StringComparison.OrdinalIgnoreCase)
				? (BitmapEncoder)new JpegBitmapEncoder()
				: new PngBitmapEncoder();

			encoder.Frames.Add(BitmapFrame.Create(renderTarget));

			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}

			using (var stream = File.Create(path))
			{
				encoder.Save(stream);
			}
		}
	}
}
