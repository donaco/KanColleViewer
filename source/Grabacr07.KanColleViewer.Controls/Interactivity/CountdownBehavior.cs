using System;
using System.Windows;

namespace Grabacr07.KanColleViewer.Interactivity
{
	/// <summary>
	/// 指定した時間までのカウントダウン機能を有する <see cref="TimerBehavior"/> です。
	/// </summary>
	public class CountdownBehavior : TimerBehavior
	{
		#region Period 依存関係プロパティ

		/// <summary>
		/// カウントダウンの終了時刻を取得または設定します。
		/// </summary>
		public DateTimeOffset? Period
		{
			get => (DateTimeOffset?)this.GetValue(PeriodProperty);
			set => this.SetValue(PeriodProperty, value);
		}

		/// <summary>
		/// <see cref="Period"/> 依存関係プロパティを識別します。
		/// </summary>
		public static readonly DependencyProperty PeriodProperty =
			DependencyProperty.Register(nameof(Period), typeof(DateTimeOffset?), typeof(CountdownBehavior), new UIPropertyMetadata(null, PeriodChangedCallback));

		private static void PeriodChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var instance = (CountdownBehavior)d;
			if (e.NewValue is DateTimeOffset period)
			{
				instance.Proc = () => DateTimeOffset.Now.Subtract(period).ToString();
				instance.Start();
			}
			else
			{
				instance.Stop();
			}
		}

		#endregion

		protected override void OnAttached()
		{
			this.Initialize();
			if (this.Period.HasValue) this.Start();
		}

		protected override void Stop()
		{
			base.Stop();
			this.AssociatedObject.Text = "";
		}
	}
}
