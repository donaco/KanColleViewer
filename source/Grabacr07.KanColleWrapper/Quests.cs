using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nekoxy;
using Grabacr07.KanColleWrapper.Internal;
using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using System.Web;

namespace Grabacr07.KanColleWrapper
{
	public class Quests : Notifier
	{
		#region All 変更通知プロパティ

		private IReadOnlyCollection<Quest> _All;

		public IReadOnlyCollection<Quest> All
		{
			get { return this._All; }
			set
			{
				if (!Equals(this._All, value))
				{
					this._All = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region Current 変更通知プロパティ

		private IReadOnlyCollection<Quest> _Current;

		/// <summary>
		/// 現在遂行中の任務のリストを取得します。未取得の任務がある場合、リスト内に null が含まれることに注意してください。
		/// </summary>
		public IReadOnlyCollection<Quest> Current
		{
			get { return this._Current; }
			set
			{
				if (!Equals(this._Current, value))
				{
					this._Current = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsUntaken 変更通知プロパティ

		private bool _IsUntaken;

		public bool IsUntaken
		{
			get { return this._IsUntaken; }
			set
			{
				if (this._IsUntaken != value)
				{
					this._IsUntaken = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion

		#region IsEmpty 変更通知プロパティ

		private bool _IsEmpty;

		public bool IsEmpty
		{
			get { return this._IsEmpty; }
			set
			{
				if (this._IsEmpty != value)
				{
					this._IsEmpty = value;
					this.RaisePropertyChanged();
				}
			}
		}

		#endregion


		internal Quests(KanColleProxy proxy)
		{
			this.IsUntaken = true;
			this.All = this.Current = new List<Quest>();

			proxy.api_get_member_questlist
				.Select(Serialize)
				.Where(x => x != null)
				.Subscribe(this.Update);
		}

		private static kcsapi_questlist Serialize(Session session)
		{
			// 既存の解析処理（変更なし）
			try
			{
				// JSON をパースして api_data を取り出す（Newtonsoft を使用）
				var json = session.GetResponseAsJson();
				JObject root;
				try
				{
					root = JObject.Parse(json);
				}
				catch (JsonException)
				{
					return null;
				}

				var data = root["api_data"];
				if (data == null)
				{
					Debug.WriteLine("Quests.Serialize: api_data not found.");
					return null;
				}

				var questlist = new kcsapi_questlist
				{
					api_count = (int?)(data["api_count"]) ?? 0,
					api_completed_kind = (int?)(data["api_completed_kind"]) ?? 0,
					api_exec_count = (int?)(data["api_exec_count"]) ?? 0,
					api_exec_type = (int?)(data["api_exec_type"]) ?? 0,
				};

				var apiListToken = data["api_list"];
				if (apiListToken != null && apiListToken.Type == JTokenType.Array)
				{
					var list = new List<kcsapi_quest>();
					var serializer = new DataContractJsonSerializer(typeof(kcsapi_quest));

					foreach (var item in apiListToken)
					{
						try
						{
							// まず既存と同じ DataContractJsonSerializer を試す
							var itemJson = item.ToString(Formatting.None);
							using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(itemJson)))
							{
								var obj = serializer.ReadObject(ms) as kcsapi_quest;
								if (obj != null)
								{
									list.Add(obj);
									continue;
								}
							}
						}
						catch (SerializationException sex)
						{
							// 一部 API の -1 埋めなどで失敗することがある（従来の実装と同じく無視）
							Debug.WriteLine(sex.Message);
						}
						catch
						{
						}

						// フォールバック：Newtonsoft で直接マッピング
						try
						{
							var obj2 = item.ToObject<kcsapi_quest>();
							if (obj2 != null) list.Add(obj2);
						}
						catch
						{
						}
					}

					questlist.api_list = list.ToArray();
				}

				return questlist;
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
				return null;
			}
		}

		// internal に変更（KanColleClient から直接呼べるようにする）
		internal void Update(kcsapi_questlist questlist)
		{
			this.IsUntaken = false;

			if (questlist.api_list == null)
			{
				this.IsEmpty = true;
				this.All = this.Current = new List<Quest>();
			}
			else
			{
				this.IsEmpty = false;

				this.All = questlist.api_list.Select(x => new Quest(x))
					.Distinct(x => x.Id)
					.OrderBy(x => x.Id)
					.ToList();

				var current = this.All.Where(x => x.State == QuestState.TakeOn || x.State == QuestState.Accomplished)
					.OrderBy(x => x.Id)
					.ToList();

				// 遂行中の任務数に満たない場合、未取得分として null で埋める
				while (current.Count < questlist.api_exec_count) current.Add(null);
				this.Current = current;
			}
		}
	}
}
