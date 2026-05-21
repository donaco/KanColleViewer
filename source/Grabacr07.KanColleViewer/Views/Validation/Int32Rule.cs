using System;
using System.Globalization;
using System.Windows.Controls;

namespace Grabacr07.KanColleViewer.Views.Controls
{
    /// <summary>
    /// 入力値が有効な Int32 かどうかを検証します。
    /// Phase 4: MetroRadiance.UI.Controls.Int32Rule の代替実装です。
    /// </summary>
    public class Int32Rule : ValidationRule
    {
        /// <summary>空文字を許可するかどうかを取得または設定します。</summary>
        public bool AllowsEmpty { get; set; }

        /// <summary>入力可能な最小値を取得または設定します。</summary>
        public int? Min { get; set; }

        /// <summary>入力可能な最大値を取得または設定します。</summary>
        public int? Max { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var str = value as string;
            if (string.IsNullOrEmpty(str))
            {
                return this.AllowsEmpty
                    ? new ValidationResult(true, null)
                    : new ValidationResult(false, "値を入力してください。");
            }

            if (!int.TryParse(str, NumberStyles.Integer, cultureInfo, out var number))
            {
                return new ValidationResult(false, "数値を入力してください。");
            }

            if (this.Min.HasValue && number < this.Min.Value)
            {
                return new ValidationResult(false, $"{this.Min} 以上の数値を入力してください。");
            }

            if (this.Max.HasValue && number > this.Max.Value)
            {
                return new ValidationResult(false, $"{this.Max} 以下の数値を入力してください。");
            }

            return new ValidationResult(true, null);
        }
    }
}
