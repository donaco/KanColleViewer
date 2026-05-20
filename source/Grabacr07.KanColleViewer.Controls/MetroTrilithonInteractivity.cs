using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using Livet.Messaging;
using Microsoft.Xaml.Behaviors;

namespace MetroTrilithon.UI.Interactivity
{
    // MetroTrilithon.Desktop Interactivity の内製化 (Phase 1)

    public class ScrollBarThresholdBehavior : Behavior<ScrollViewer>
    {
        private ScrollBarVisibility? _hsbvBackup;
        private ScrollBarVisibility? _vsbvBackup;
        private double? _maxwBackup;
        private double? _maxhBackup;

        public double Horizontal
        {
            get { return (double)this.GetValue(HorizontalProperty); }
            set { this.SetValue(HorizontalProperty, value); }
        }
        public static readonly DependencyProperty HorizontalProperty =
            DependencyProperty.Register(nameof(Horizontal), typeof(double), typeof(ScrollBarThresholdBehavior), new PropertyMetadata(.0));

        public double Vertical
        {
            get { return (double)this.GetValue(VerticalProperty); }
            set { this.SetValue(VerticalProperty, value); }
        }
        public static readonly DependencyProperty VerticalProperty =
            DependencyProperty.Register(nameof(Vertical), typeof(double), typeof(ScrollBarThresholdBehavior), new PropertyMetadata(.0));

        protected override void OnAttached()
        {
            base.OnAttached();
            this.AssociatedObject.SizeChanged += this.HandleSizeChanged;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            this.AssociatedObject.SizeChanged -= this.HandleSizeChanged;
        }

        private void HandleSizeChanged(object sender, SizeChangedEventArgs args)
        {
            if (this.Horizontal > .0)
            {
                if (args.NewSize.Width < this.Horizontal)
                {
                    if (this._hsbvBackup == null) this._hsbvBackup = this.AssociatedObject.HorizontalScrollBarVisibility;
                    if (this._maxwBackup == null) this._maxwBackup = ((FrameworkElement)this.AssociatedObject.Content).MaxWidth;
                    this.AssociatedObject.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
                    ((FrameworkElement)this.AssociatedObject.Content).MaxWidth = this.Horizontal;
                }
                else
                {
                    if (this._hsbvBackup != null) this.AssociatedObject.HorizontalScrollBarVisibility = this._hsbvBackup.Value;
                    if (this._maxwBackup != null) ((FrameworkElement)this.AssociatedObject.Content).MaxWidth = this._maxwBackup.Value;
                }
            }
            if (this.Vertical > .0)
            {
                if (args.NewSize.Height < this.Vertical)
                {
                    if (this._vsbvBackup == null) this._vsbvBackup = this.AssociatedObject.VerticalScrollBarVisibility;
                    if (this._maxhBackup == null) this._maxhBackup = ((FrameworkElement)this.AssociatedObject.Content).MaxHeight;
                    this.AssociatedObject.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
                    ((FrameworkElement)this.AssociatedObject.Content).MaxHeight = this.Vertical;
                }
                else
                {
                    if (this._vsbvBackup != null) this.AssociatedObject.VerticalScrollBarVisibility = this._vsbvBackup.Value;
                    if (this._maxhBackup != null) ((FrameworkElement)this.AssociatedObject.Content).MaxHeight = this._maxhBackup.Value;
                }
            }
        }
    }

    public class TaskbarMessage : InteractionMessage
    {
        public TaskbarItemProgressState? ProgressState
        {
            get { return (TaskbarItemProgressState?)this.GetValue(ProgressStateProperty); }
            set { this.SetValue(ProgressStateProperty, value); }
        }
        public static readonly DependencyProperty ProgressStateProperty =
            DependencyProperty.Register(nameof(ProgressState), typeof(TaskbarItemProgressState?), typeof(TaskbarMessage), new UIPropertyMetadata(null));

        public double? ProgressValue
        {
            get { return (double?)this.GetValue(ProgressValueProperty); }
            set { this.SetValue(ProgressValueProperty, value); }
        }
        public static readonly DependencyProperty ProgressValueProperty =
            DependencyProperty.Register(nameof(ProgressValue), typeof(double?), typeof(TaskbarMessage), new UIPropertyMetadata(null));

        public ImageSource Overlay
        {
            get { return (ImageSource)this.GetValue(OverlayProperty); }
            set { this.SetValue(OverlayProperty, value); }
        }
        public static readonly DependencyProperty OverlayProperty =
            DependencyProperty.Register(nameof(Overlay), typeof(ImageSource), typeof(TaskbarMessage), new UIPropertyMetadata(null));

        public string Description
        {
            get { return (string)this.GetValue(DescriptionProperty); }
            set { this.SetValue(DescriptionProperty, value); }
        }
        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(TaskbarMessage), new UIPropertyMetadata(null));

        public Thickness? ThumbnailClipMargin
        {
            get { return (Thickness?)this.GetValue(ThumbnailClipMarginProperty); }
            set { this.SetValue(ThumbnailClipMarginProperty, value); }
        }
        public static readonly DependencyProperty ThumbnailClipMarginProperty =
            DependencyProperty.Register(nameof(ThumbnailClipMargin), typeof(Thickness?), typeof(TaskbarMessage), new UIPropertyMetadata(null));

        public TaskbarItemThumbButtonInfoCollection ThumbButtonInfos
        {
            get { return (TaskbarItemThumbButtonInfoCollection)this.GetValue(ThumbButtonInfosProperty); }
            set { this.SetValue(ThumbButtonInfosProperty, value); }
        }
        public static readonly DependencyProperty ThumbButtonInfosProperty =
            DependencyProperty.Register(nameof(ThumbButtonInfos), typeof(TaskbarItemThumbButtonInfoCollection), typeof(TaskbarMessage), new UIPropertyMetadata(null));

        protected override Freezable CreateInstanceCore() => new TaskbarMessage();
    }

    public class TaskbarMessageAction : TriggerAction<Window>
    {
        public bool InvokeActionOnlyWhenWindowIsActive
        {
            get { return (bool)this.GetValue(InvokeActionOnlyWhenWindowIsActiveProperty); }
            set { this.SetValue(InvokeActionOnlyWhenWindowIsActiveProperty, value); }
        }
        public static readonly DependencyProperty InvokeActionOnlyWhenWindowIsActiveProperty =
            DependencyProperty.Register(nameof(InvokeActionOnlyWhenWindowIsActive), typeof(bool), typeof(TaskbarMessageAction), new UIPropertyMetadata(true));

        protected override void Invoke(object parameter)
        {
            if (this.InvokeActionOnlyWhenWindowIsActive && !this.AssociatedObject.IsActive) return;
            if (!(parameter is InteractionMessage interactionMessage)) return;

            var message = interactionMessage as TaskbarMessage;
            if (message == null) return;

            var taskbarInfo = this.AssociatedObject.TaskbarItemInfo
                ?? (this.AssociatedObject.TaskbarItemInfo = new TaskbarItemInfo());

            if (message.ProgressState != null) taskbarInfo.ProgressState = message.ProgressState.Value;
            if (message.ProgressValue != null) taskbarInfo.ProgressValue = message.ProgressValue.Value;
            if (message.Overlay != null) taskbarInfo.Overlay = message.Overlay;
            if (message.Description != null) taskbarInfo.Description = message.Description;
            if (message.ThumbnailClipMargin != null) taskbarInfo.ThumbnailClipMargin = message.ThumbnailClipMargin.Value;
            if (message.ThumbButtonInfos != null) taskbarInfo.ThumbButtonInfos = message.ThumbButtonInfos;
        }
    }
}
