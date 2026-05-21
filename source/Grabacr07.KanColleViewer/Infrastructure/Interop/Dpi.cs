using System.Windows;
using System.Windows.Media;

namespace Grabacr07.KanColleViewer.Infrastructure.Interop
{
    /// <summary>
    /// モニターの DPI を表します。
    /// Phase 4: MetroRadiance.Interop.Dpi の代替実装です。
    /// </summary>
    public struct Dpi
    {
        public static readonly Dpi Default = new Dpi(96, 96);

        private double? _scaleX;
        private double? _scaleY;

        public uint X { get; }
        public uint Y { get; }

        public double ScaleX => this._scaleX ?? (this._scaleX = this.X / (double)Default.X).Value;
        public double ScaleY => this._scaleY ?? (this._scaleY = this.Y / (double)Default.Y).Value;

        public Dpi(uint x, uint y) : this()
        {
            this.X = x;
            this.Y = y;
        }

        /// <summary>
        /// <see cref="Visual"/> からシステム DPI を取得します。
        /// </summary>
        public static Dpi? GetSystemDpi(Visual visual)
        {
            var source = PresentationSource.FromVisual(visual);
            if (source?.CompositionTarget != null)
            {
                return new Dpi(
                    (uint)(Default.X * source.CompositionTarget.TransformToDevice.M11),
                    (uint)(Default.Y * source.CompositionTarget.TransformToDevice.M22));
            }
            return null;
        }
    }
}
