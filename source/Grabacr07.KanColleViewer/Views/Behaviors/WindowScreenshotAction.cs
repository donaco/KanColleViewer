using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Grabacr07.KanColleViewer.Models;
using Grabacr07.KanColleViewer.Properties;
using MetroTrilithon.Mvvm;
using Microsoft.Xaml.Behaviors;

namespace Grabacr07.KanColleViewer.Views.Behaviors
{
    /// <summary>
    /// ウィンドウ全体を画像として保存する機能を提供します。
    /// </summary>
    internal class WindowScreenshotAction : Behavior<Window>
    {
        #region ViewModel 依存関係プロパティ

        public WindowViewModel ViewModel
        {
            get { return (WindowViewModel)this.GetValue(ViewModelProperty); }
            set { this.SetValue(ViewModelProperty, value); }
        }
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(nameof(ViewModel), typeof(WindowViewModel), typeof(WindowScreenshotAction), new UIPropertyMetadata(null, OnViewModelChanged));

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var action = (WindowScreenshotAction)d;
            if (e.OldValue is WindowViewModel old) old.ScreenshotRequested -= action.OnScreenshotRequested;
            if (e.NewValue is WindowViewModel vm) vm.ScreenshotRequested += action.OnScreenshotRequested;
        }

        #endregion

        protected override void OnDetaching()
        {
            if (this.ViewModel != null) this.ViewModel.ScreenshotRequested -= this.OnScreenshotRequested;
            base.OnDetaching();
        }

        private void OnScreenshotRequested(object sender, ScreenshotRequestedEventArgs e)
        {
            try
            {
                this.CaptureWindow(e.Path, (SupportedImageFormat)e.Format);
                StatusService.Current.Notify(Resources.Screenshot_Saved + Path.GetFileName(e.Path));
            }
            catch (Exception ex)
            {
                StatusService.Current.Notify(Resources.Screenshot_Failed + ex.Message);
                System.Diagnostics.Debug.WriteLine(ex);
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
