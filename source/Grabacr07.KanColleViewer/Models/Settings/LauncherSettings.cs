using System;
using System.Runtime.CompilerServices;
using MetroTrilithon.Serialization;

namespace Grabacr07.KanColleViewer.Models.Settings
{
	/// <summary>
	/// ウィンドウランチャーのチェック状態を表す静的プロパティを公開します。
	/// </summary>
	public static class LauncherSettings
	{
		public static SerializableProperty<bool> ShowWindowsOnStartup { get; }
			= new SerializableProperty<bool>(GetKey(), Providers.Roaming, false);

		public static SerializableProperty<bool> ShowWindowLauncher { get; }
			= new SerializableProperty<bool>(GetKey(), Providers.Roaming, true);

		public static SerializableProperty<bool> ShipCatalog { get; }
			= new SerializableProperty<bool>(GetKey(), Providers.Roaming, false);

		public static SerializableProperty<bool> SlotItemCatalog { get; }
			= new SerializableProperty<bool>(GetKey(), Providers.Roaming, false);

		public static SerializableProperty<bool> Quests { get; }
			= new SerializableProperty<bool>(GetKey(), Providers.Roaming, false);

		public static SerializableProperty<bool> Fleets { get; }
			= new SerializableProperty<bool>(GetKey(), Providers.Roaming, false);

		public static SerializableProperty<bool> AirBases { get; }
			= new SerializableProperty<bool>(GetKey(), Providers.Roaming, false);

		public static SerializableProperty<bool> Counter { get; }
			= new SerializableProperty<bool>(GetKey(), Providers.Roaming, false);

		public static SerializableProperty<bool> MapHp { get; }
			= new SerializableProperty<bool>(GetKey(), Providers.Roaming, false);

		public static SerializableProperty<bool> MasterView { get; }
			= new SerializableProperty<bool>(GetKey(), Providers.Roaming, false);

		public static SerializableProperty<bool> Material { get; }
			= new SerializableProperty<bool>(GetKey(), Providers.Roaming, false);


		private static string GetKey([CallerMemberName] string propertyName = "")
		{
			return nameof(LauncherSettings) + "." + propertyName;
		}
	}
}
