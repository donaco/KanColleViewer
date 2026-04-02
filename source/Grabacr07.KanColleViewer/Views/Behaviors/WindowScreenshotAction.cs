using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Properties;
using Grabacr07.KanColleViewer.ViewModels.Messages;
using Livet.Behaviors.Messaging;
using Livet.Messaging;

namespace Grabacr07.KanColleViewer.Views.Behaviors
{
    /// <summary>
    /// ウィンドウ全体を画像として保存する機能を提供します。
    /// </summary>
    internal class WindowScreenshotAction : InteractionMessageAction<Window>
    {
        protected override void InvokeAction(InteractionMessage message)
        {
            if (message is ScreenshotMessage screenshotMessage)
            {
                try
                {
                    this.CaptureWindow(screenshotMessage.Path, screenshotMessage.Format);
                    StatusService.Current.Notify(Resources.Screenshot_Saved + Path.GetFileName(screenshotMessage.Path));
                }
                catch (Exception ex)
                {
                    StatusService.Current.Notify(Resources.Screenshot_Failed + ex.Message);
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }
        }

        private void CaptureWindow(string path, SupportedImageFormat format)
        {
            var window = this.AssociatedObject;
            if (window == null)
                throw new InvalidOperationException("対象のウィンドウが見つかりません。");

            // ウィンドウの DPI スケーリングを考慮
            var source = PresentationSource.FromVisual(window);
            var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            var width = window.ActualWidth;
            var height = window.ActualHeight;

            var renderWidth = (int)(width * dpiX);
            var renderHeight = (int)(height * dpiY);

            var renderTarget = new RenderTargetBitmap(renderWidth, renderHeight, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
            renderTarget.Render(window);

            BitmapEncoder encoder;
            switch (format)
            {
                case SupportedImageFormat.Jpeg:
                    encoder = new JpegBitmapEncoder();
                    break;
                case SupportedImageFormat.Png:
                default:
                    encoder = new PngBitmapEncoder();
                    break;
            }

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
