using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;

// MetroTrilithon.UI.Controls (CallMethodButton / SortButton / TabHeader / RichTextView / HyperlinkEx / RichText系)
namespace MetroTrilithon.UI.Controls
{
    // RichText 系（PluginViewModel / RichText.cs から MetroTrilithon.UI.Controls で参照される）
    public abstract class RichText
    {
        public string Text { get; set; }
    }

    public abstract class Link : RichText
    {
        public abstract void Click();
    }

    public class Regular : RichText { }

    /// <summary>
    /// クリックされたときに、指定したメソッドを実行する <see cref="Button"/> を表します。
    /// </summary>
    public class CallMethodButton : Button
    {
        static CallMethodButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CallMethodButton), new FrameworkPropertyMetadata(typeof(CallMethodButton)));
        }

        private bool _hasParameter;

        #region MethodTarget 依存関係プロパティ
        public object MethodTarget
        {
            get { return this.GetValue(MethodTargetProperty); }
            set { this.SetValue(MethodTargetProperty, value); }
        }
        public static readonly DependencyProperty MethodTargetProperty =
            DependencyProperty.Register(nameof(MethodTarget), typeof(object), typeof(CallMethodButton), new UIPropertyMetadata(null));
        #endregion

        #region MethodName 依存関係プロパティ
        public string MethodName
        {
            get { return (string)this.GetValue(MethodNameProperty); }
            set { this.SetValue(MethodNameProperty, value); }
        }
        public static readonly DependencyProperty MethodNameProperty =
            DependencyProperty.Register(nameof(MethodName), typeof(string), typeof(CallMethodButton), new UIPropertyMetadata(null));
        #endregion

        #region MethodParameter 依存関係プロパティ
        public object MethodParameter
        {
            get { return this.GetValue(MethodParameterProperty); }
            set { this.SetValue(MethodParameterProperty, value); }
        }
        public static readonly DependencyProperty MethodParameterProperty =
            DependencyProperty.Register(nameof(MethodParameter), typeof(object), typeof(CallMethodButton), new UIPropertyMetadata(null, (d, e) => ((CallMethodButton)d)._hasParameter = true));
        #endregion

        protected override void OnClick()
        {
            base.OnClick();
            if (string.IsNullOrEmpty(this.MethodName)) return;
            var target = this.MethodTarget ?? this.DataContext;
            if (target == null) return;
            try
            {
                if (this._hasParameter) InvokeMethod(target, this.MethodName, this.MethodParameter);
                else InvokeMethod(target, this.MethodName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CallMethodButton] {this.MethodName}: {ex.Message}");
            }
        }

        private static void InvokeMethod(object target, string methodName)
        {
            var method = target.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            method?.Invoke(target, null);
        }

        private static void InvokeMethod(object target, string methodName, object parameter)
        {
            var paramType = parameter?.GetType() ?? typeof(object);
            var method = target.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { paramType }, null)
                ?? target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.Name == methodName && m.GetParameters().Length == 1)
                    .FirstOrDefault(m => m.GetParameters()[0].ParameterType.IsAssignableFrom(paramType));
            method?.Invoke(target, new[] { parameter });
        }
    }

    public enum SortDirection { None, Ascending, Descending }

    public class SortButton : CallMethodButton
    {
        static SortButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SortButton), new FrameworkPropertyMetadata(typeof(SortButton)));
        }

        public SortDirection Direction
        {
            get { return (SortDirection)this.GetValue(DirectionProperty); }
            set { this.SetValue(DirectionProperty, value); }
        }
        public static readonly DependencyProperty DirectionProperty =
            DependencyProperty.Register(nameof(Direction), typeof(SortDirection), typeof(SortButton), new UIPropertyMetadata(SortDirection.None));
    }

    public class TabHeader : ListBox
    {
        static TabHeader()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TabHeader), new FrameworkPropertyMetadata(typeof(TabHeader)));
        }
    }

    [ContentProperty("Content")]
    public class RichTextInlinePresenter : ContentPresenter { }

    [ContentProperty(nameof(RichTextTemplates))]
    public class RichTextView : RichTextBox
    {
        static RichTextView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(RichTextView), new FrameworkPropertyMetadata(typeof(RichTextView)));
        }

        public IEnumerable<RichText> Source
        {
            get { return (IEnumerable<RichText>)this.GetValue(SourceProperty); }
            set { this.SetValue(SourceProperty, value); }
        }
        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(nameof(Source), typeof(IEnumerable<RichText>), typeof(RichTextView),
                new UIPropertyMetadata(null, (d, e) => ((RichTextView)d).UpdateDocument()));

        public Collection<DataTemplate> RichTextTemplates
        {
            get { return (Collection<DataTemplate>)this.GetValue(RichTextTemplatesProperty); }
            set { this.SetValue(RichTextTemplatesProperty, value); }
        }
        public static readonly DependencyProperty RichTextTemplatesProperty =
            DependencyProperty.Register(nameof(RichTextTemplates), typeof(Collection<DataTemplate>), typeof(RichTextView),
                new UIPropertyMetadata(new Collection<DataTemplate>(), (d, e) => ((RichTextView)d).UpdateDocument()));

        public RichTextView() { this.Loaded += (s, e) => this.UpdateDocument(); }

        private void UpdateDocument()
        {
            if (this.Source == null || this.RichTextTemplates == null || !this.RichTextTemplates.Any()) return;
            var paragraph = new Paragraph();
            foreach (var rt in this.Source)
            {
                var template = this.RichTextTemplates.FirstOrDefault(dt => (dt.DataType as Type) == rt.GetType());
                var presenter = template?.LoadContent() as RichTextInlinePresenter;
                var inline = presenter?.Content as Inline;
                if (inline != null) { inline.DataContext = rt; paragraph.Inlines.Add(inline); }
            }
            this.Document = new FlowDocument(paragraph) { TextAlignment = TextAlignment.Left };
        }

    }

    public class WebBrowserHelper
    {
        public static readonly DependencyProperty ScriptErrorsSuppressedProperty =
            DependencyProperty.RegisterAttached("ScriptErrorsSuppressed", typeof(bool), typeof(WebBrowserHelper), new PropertyMetadata(default(bool), ScriptErrorsSuppressedChangedCallback));

        public static void SetScriptErrorsSuppressed(WebBrowser browser, bool value) => browser.SetValue(ScriptErrorsSuppressedProperty, value);
        public static bool GetScriptErrorsSuppressed(WebBrowser browser) => (bool)browser.GetValue(ScriptErrorsSuppressedProperty);

        private static void ScriptErrorsSuppressedChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebBrowser browser && e.NewValue is bool)
            {
                try
                {
                    var ax = GetAxWebbrowser2(browser);
                    ax?.GetType().InvokeMember("Silent", BindingFlags.SetProperty, null, ax, new[] { e.NewValue });
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }

        public static readonly DependencyProperty AllowWebBrowserDropProperty =
            DependencyProperty.RegisterAttached("AllowWebBrowserDrop", typeof(bool), typeof(WebBrowserHelper), new PropertyMetadata(true, AllowWebBrowserDropChangedCallback));

        public static void SetAllowWebBrowserDrop(DependencyObject element, bool value) => element.SetValue(AllowWebBrowserDropProperty, value);
        public static bool GetAllowWebBrowserDrop(DependencyObject element) => (bool)element.GetValue(AllowWebBrowserDropProperty);

        private static void AllowWebBrowserDropChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebBrowser browser && e.NewValue is bool)
            {
                try
                {
                    var ax = GetAxWebbrowser2(browser);
                    ax?.GetType().InvokeMember("RegisterAsDropTarget", BindingFlags.SetProperty, null, ax, new[] { e.NewValue });
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }

        public static object GetAxWebbrowser2(WebBrowser browser)
        {
            var prop = typeof(WebBrowser).GetProperty("AxIWebBrowser2", BindingFlags.Instance | BindingFlags.NonPublic);
            return prop?.GetValue(browser, null);
        }
    }

    public class HyperlinkEx : Hyperlink
    {
        public Uri Uri
        {
            get { return (Uri)this.GetValue(UriProperty); }
            set { this.SetValue(UriProperty, value); }
        }
        public static readonly DependencyProperty UriProperty =
            DependencyProperty.Register(nameof(Uri), typeof(Uri), typeof(HyperlinkEx), new UIPropertyMetadata(null));

        protected override void OnClick()
        {
            base.OnClick();
            if (this.Uri != null)
            {
                try { System.Diagnostics.Process.Start(this.Uri.ToString()); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }
    }

    public static class Extensions
    {
        public static System.Windows.Controls.Dock Reverse(this System.Windows.Controls.Dock d)
        {
            switch (d)
            {
                case System.Windows.Controls.Dock.Top: return System.Windows.Controls.Dock.Bottom;
                case System.Windows.Controls.Dock.Left: return System.Windows.Controls.Dock.Right;
                case System.Windows.Controls.Dock.Right: return System.Windows.Controls.Dock.Left;
                case System.Windows.Controls.Dock.Bottom: return System.Windows.Controls.Dock.Top;
            }
            return d;
        }
    }
}
