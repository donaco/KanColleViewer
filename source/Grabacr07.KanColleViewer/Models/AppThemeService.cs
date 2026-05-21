using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Grabacr07.KanColleViewer.Models
{
    /// <summary>
    /// アプリケーション テーマを管理します。
    /// Phase 4: MetroRadiance.UI.ThemeService の代替実装です。
    /// </summary>
    public class AppThemeService
    {
        public static AppThemeService Current { get; } = new AppThemeService();

        private static readonly Uri _baseUri = new Uri("pack://application:,,,/KanColleViewer;component/Styles/Themes/");

        // アクセントカラーごとのリソースキーと色の対応
        private static readonly Dictionary<AppAccent, (Color accent, Color highlight, Color active)> _accentColors
            = new Dictionary<AppAccent, (Color, Color, Color)>
            {
                [AppAccent.Purple] = (Color.FromRgb(0x68, 0x21, 0x7A), Color.FromRgb(0x8C, 0x46, 0xA0), Color.FromRgb(0x5A, 0x14, 0x64)),
                [AppAccent.Blue]   = (Color.FromRgb(0x00, 0x7A, 0xCC), Color.FromRgb(0x28, 0xA0, 0xF0), Color.FromRgb(0x00, 0x5A, 0xAA)),
                [AppAccent.Orange] = (Color.FromRgb(0xCA, 0x51, 0x00), Color.FromRgb(0xF0, 0x78, 0x28), Color.FromRgb(0xB4, 0x3C, 0x00)),
            };

        private Application _app;
        private AppAccent _currentAccent = AppAccent.Purple;

        private AppThemeService() { }

        /// <summary>
        /// テーマサービスを初期化します。
        /// </summary>
        public void Register(Application app, AppAccent accent = AppAccent.Purple)
        {
            this._app = app ?? throw new ArgumentNullException(nameof(app));
            this._currentAccent = accent;
        }

        /// <summary>
        /// アクセントカラーを変更します。アプリケーションの ResourceDictionary を直接書き換えます。
        /// </summary>
        public void ChangeAccent(AppAccent accent)
        {
            if (this._app == null) return;
            if (this._currentAccent == accent) return;

            this._currentAccent = accent;
            this.ApplyAccent(accent);
        }

        /// <summary>
        /// 任意の Color でアクセントを変更します。
        /// </summary>
        public void ChangeAccent(Color color)
        {
            if (this._app == null) return;

            // ハイライト（+20%明度）・アクティブ（-10%明度）を自動算出
            var highlight = LightenColor(color, 0.2f);
            var active    = LightenColor(color, -0.1f);

            this._app.Dispatcher.Invoke(() =>
            {
                SetBrushResource(_app.Resources, "AccentColorKey",          color);
                SetBrushResource(_app.Resources, "AccentBrushKey",          color);
                SetBrushResource(_app.Resources, "AccentHighlightColorKey",  highlight);
                SetBrushResource(_app.Resources, "AccentHighlightBrushKey",  highlight);
                SetBrushResource(_app.Resources, "AccentActiveColorKey",     active);
                SetBrushResource(_app.Resources, "AccentActiveBrushKey",     active);
            });
        }

        private void ApplyAccent(AppAccent accent)
        {
            if (!_accentColors.TryGetValue(accent, out var colors)) return;

            this._app.Dispatcher.Invoke(() =>
            {
                SetBrushResource(_app.Resources, "AccentColorKey",          colors.accent);
                SetBrushResource(_app.Resources, "AccentBrushKey",          colors.accent);
                SetBrushResource(_app.Resources, "AccentHighlightColorKey",  colors.highlight);
                SetBrushResource(_app.Resources, "AccentHighlightBrushKey",  colors.highlight);
                SetBrushResource(_app.Resources, "AccentActiveColorKey",     colors.active);
                SetBrushResource(_app.Resources, "AccentActiveBrushKey",     colors.active);
            });
        }

        private static void SetBrushResource(ResourceDictionary rd, string key, Color color)
        {
            // MergedDictionaries を再帰的に検索して上書き
            SetBrushResourceCore(rd, key, color);
        }

        private static void SetBrushResourceCore(ResourceDictionary rd, string key, Color color)
        {
            if (rd.Contains(key))
            {
                if (rd[key] is SolidColorBrush brush)
                {
                    // フリーズ済みブラシは新しいインスタンスで置き換える
                    if (brush.IsFrozen)
                        rd[key] = new SolidColorBrush(color);
                    else
                        brush.Color = color;
                }
                else if (rd[key] is Color)
                {
                    rd[key] = color;
                }
            }

            foreach (var merged in rd.MergedDictionaries)
            {
                SetBrushResourceCore(merged, key, color);
            }
        }

        private static Color LightenColor(Color color, float amount)
        {
            float r = Math.Max(0, Math.Min(1, color.ScR + amount));
            float g = Math.Max(0, Math.Min(1, color.ScG + amount));
            float b = Math.Max(0, Math.Min(1, color.ScB + amount));
            return Color.FromScRgb(1f, r, g, b);
        }
    }

    /// <summary>
    /// アプリケーションのアクセントカラーを表します。
    /// Phase 4: MetroRadiance.UI.Accent の代替です。
    /// </summary>
    public enum AppAccent
    {
        Purple,
        Blue,
        Orange,
        /// <summary>任意の Color を使う場合は AppThemeService.ChangeAccent(Color) を使用します。</summary>
        Custom,
    }
}
