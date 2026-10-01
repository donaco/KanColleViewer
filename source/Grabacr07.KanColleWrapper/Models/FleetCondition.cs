using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Grabacr07.KanColleWrapper.Models
{
	public class FleetCondition : TimerNotifier
	{
		private bool notificated;
		private int minCondition;

		/// <summary>
		/// 通知機能が有効かどうかを示す値を取得または設定します。
		/// </summary>
		public bool IsEnabled { get; set; }

		public string Name { get; set; } = null!;

		#region RejuvenateTime / IsRejuvenating 変更通知プロパティ

		/// <summary>
		/// 疲労回復の目安時間を取得します。
		/// </summary>
		public DateTimeOffset? RejuvenateTime
		{
			get => field;
			private set
			{
				if (field != value)
				{
					field = value;
					this.notificated = false;
					this.RaisePropertyChanged();
					this.RaisePropertyChanged(nameof(this.IsRejuvenating));
				}
			}
		}

		/// <summary>
		/// 艦隊に編成されている艦娘の疲労を自然回復しているかどうかを示す値を取得します。
		/// </summary>
		public bool IsRejuvenating => this.RejuvenateTime.HasValue;

		#endregion

		#region Remaining 変更通知プロパティ

		/// <summary>
		/// 疲労の回復が完了するまでの残り時間を取得します。1 秒ごとに更新されます。
		/// </summary>
		public TimeSpan? Remaining
		{
			get => field;
			private set
			{
				if (field != value)
				{
					field = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		public event EventHandler<ConditionRejuvenatedEventArgs>? Rejuvenated;

		internal void Update(Ship[] s)
		{
			if (s.Length == 0)
			{
				this.RejuvenateTime = null;
				return;
			}

			var condition = s.Min(x => x.Condition);
			if (condition != this.minCondition)
			{
				this.minCondition = condition;

				var rejuvnate = DateTimeOffset.Now; // 回復完了予測時刻

				while (condition < Math.Min(49, KanColleClient.Current.Settings.ReSortieCondition))
				{
					rejuvnate = rejuvnate.AddMinutes(3);
					condition += 3;
					if (condition > 49) condition = 49;
				}

				this.RejuvenateTime = rejuvnate <= DateTimeOffset.Now
					? (DateTimeOffset?)null
					: rejuvnate;
			}
		}


		protected override void Tick()
		{
			base.Tick();

			if (this.RejuvenateTime.HasValue && this.IsEnabled)
			{
				var remaining = this.RejuvenateTime.Value.Subtract(DateTimeOffset.Now);
				if (remaining.Ticks < 0) remaining = TimeSpan.Zero;

				this.Remaining = remaining;

				if (!this.notificated && this.Rejuvenated != null && remaining.Ticks <= 0)
				{
					this.Rejuvenated(this, new ConditionRejuvenatedEventArgs(this.Name, 0));
					this.notificated = true;
				}
			}
			else
			{
				this.Remaining = null;
			}
		}
	}
}
