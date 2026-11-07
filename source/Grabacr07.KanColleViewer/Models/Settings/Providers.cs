using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MetroTrilithon.Serialization;

namespace Grabacr07.KanColleViewer.Models.Settings
{
	public static class Providers
	{
		private const string migrationKey = "SettingsMigration.RoamingToLocalCompleted";
		internal static string LegacyRoamingCompanyDirectoryPath { get; } = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"grabacr.net");
		private static readonly string legacyRoamingFilePath = Path.Combine(LegacyRoamingCompanyDirectoryPath, "KanColleViewer", "Settings.xaml");

		public static string LocalFilePath { get; } = Path.Combine(
			Application.Instance?.LocalAppData.FullName ?? Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				ProductInfo.Company, ProductInfo.Product),
			"Settings.xaml");

		private static readonly FileSettingsProvider provider = new FileSettingsProvider(LocalFilePath);

		public static ISerializationProvider Local => provider;

		// 既存の設定定義の名前は維持し、保存先とインスタンスは Local に統一する。
		public static ISerializationProvider Roaming => Local;

		public static void Load()
		{
			provider.Load();
			if (provider.TryGetValue<bool>(migrationKey, out var completed) && completed) return;

			if (File.Exists(legacyRoamingFilePath))
				provider.ImportMissing(legacyRoamingFilePath);

			provider.SetValue(migrationKey, true);
			try
			{
				provider.Save();
			}
			catch
			{
				provider.RemoveValue(migrationKey);
				throw;
			}
		}
	}
}
