using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nekoxy;

namespace Grabacr07.KanColleWrapper.Internal
{
	internal static class Extensions
	{
		public static string GetResponseAsJson(this Session session)
		{
			// 新: NormalizeSvDataString を使って堅牢に抽出する
			// return session.Response.BodyAsString.Replace("svdata=", "");
			return NormalizeSvDataString(session?.Response?.BodyAsString);
		}

		/// <summary>
		/// Cef などで捕捉した生レスポンス文字列を Nekoxy と同等の JSON 部分に正規化します。
		/// - "svdata=" を削る
		/// - 先頭の '{' から対応する '}' までを波括弧のバランスで切り出す（文字列リテラル内の '{' '}' を無視するよう改善）
		/// </summary>
		public static string NormalizeSvDataString(string s)
		{
			if (string.IsNullOrEmpty(s)) return null;

			// svdata= を取り除く（存在すれば）
			var t = s.Replace("svdata=", "");

			// 最初の '{' を探す（throw 1; プレフィックス等を排除）
			var first = t.IndexOf('{');
			if (first < 0) return null;

			// 波括弧でバランスをとって切り出す（文字列リテラル内の '{' '}' を無視、エスケープも考慮）
			int depth = 0;
			bool inString = false;
			bool escape = false;
			int i;
			for (i = first; i < t.Length; i++)
			{
				char ch = t[i];

				if (escape)
				{
					// 直前がバックスラッシュによるエスケープ → 文字列内の特殊文字は無視して継続
					escape = false;
					continue;
				}

				if (ch == '\\')
				{
					// 次の文字はエスケープされる
					escape = true;
					continue;
				}

				if (ch == '"')
				{
					// 文字列リテラルの開始/終了をトグル
					inString = !inString;
					continue;
				}

				if (inString)
				{
					// 文字列内にある波括弧は無視
					continue;
				}

				if (ch == '{')
				{
					depth++;
				}
				else if (ch == '}')
				{
					depth--;
					if (depth == 0)
					{
						i++; // include this closing brace
						break;
					}
				}
			}

			if (depth != 0) return null;

			return t.Substring(first, i - first).Trim();
		}

		/// <summary>
		/// <see cref="Int32" /> 型の配列に安全にアクセスします。
		/// </summary>
		public static int? Get(this int[] array, int index)
		{
			return array?.Length > index ? (int?)array[index] : null;
		}
	}
}
