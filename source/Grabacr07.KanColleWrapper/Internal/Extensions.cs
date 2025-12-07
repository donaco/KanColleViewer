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
			// NormalizeSvDataString を RetryObservableExtensions 側に委譲
			return RetryObservableExtensions.NormalizeSvDataString(session?.Response?.BodyAsString);
		}

		/// <summary>
		/// 既存の正規化実装は RetryObservableExtensions に移譲しました。
		/// </summary>
		public static string NormalizeSvDataString(string s)
		{
			return RetryObservableExtensions.NormalizeSvDataString(s);
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
