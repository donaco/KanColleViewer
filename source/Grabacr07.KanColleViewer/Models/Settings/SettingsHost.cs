using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using MetroTrilithon.Serialization;

namespace Grabacr07.KanColleViewer.Models.Settings
{
	public abstract class SettingsHost
	{
		private static readonly Dictionary<Type, SettingsHost> instances = new Dictionary<Type, SettingsHost>();
		private readonly Dictionary<string, object> cachedProperties = new Dictionary<string, object>();

		protected virtual string CategoryName => this.GetType().Name;

		protected SettingsHost()
		{
			instances[this.GetType()] = this;
		}

		/// <summary>
		/// 現在のインスタンスにキャッシュされている <see cref="SerializableProperty{T}"/>
		/// を取得します。 キャッシュがない場合は <see cref="create"/> に従って生成します。
		/// </summary>
		/// <returns></returns>
		protected SerializableProperty<T> Cache<T>(Func<string, SerializableProperty<T>> create, [CallerMemberName] string propertyName = "")
		{
			var key = this.CategoryName + "." + propertyName;

			object obj;
			if (this.cachedProperties.TryGetValue(key, out obj) && obj is SerializableProperty<T>) return (SerializableProperty<T>)obj;

			var property = create(key);
			this.cachedProperties[key] = property;

			return property;
		}

		#region Load / Save

		public static void Load()
		{
			try
			{
				Providers.Local.Load();
			}
			catch (Exception)
			{
				File.Delete(Providers.LocalFilePath);
				Providers.Local.Load();
			}

			try
			{
				Providers.Roaming.Load();
			}
			catch (Exception)
			{
				File.Delete(Providers.RoamingFilePath);
				Providers.Roaming.Load();
			}

#pragma warning disable 612
			// 古い設定が存在する可能性があるので、読んでおく
			// (ただし、1 度読んだら新しい方に移行するので保存はしない
			Migration._Settings.Load();
#pragma warning restore 612

			// 読み込んだ設定値の妥当性を検証し、不正値はデフォルトにリセットする
			SanitizeSettings();
		}

		/// <summary>
		/// 読み込んだ設定値を検証し、範囲外の値をデフォルト値にリセットします。
		/// </summary>
		private static void SanitizeSettings()
		{
			// ポート番号の有効範囲 (1～65535)
			// ushort 型は 0～65535 のため、0 のみ不正値として扱う

			// ローカル待ち受けポート（現在未使用。設定値の破損防止のため保持）
			if (NetworkSettings.LocalProxy.Port.Value == 0)
				NetworkSettings.LocalProxy.Port.Value = NetworkSettings.LocalProxy.Port.Default;

			// 上流プロキシ: HTTP ポート
			if (NetworkSettings.Proxy.Port.Value == 0)
				NetworkSettings.Proxy.Port.Value = NetworkSettings.Proxy.Port.Default;

			// 上流プロキシ: HTTPS ポート
			if (NetworkSettings.Proxy.HttpsPort.Value == 0)
				NetworkSettings.Proxy.HttpsPort.Value = NetworkSettings.Proxy.HttpsPort.Default;

			// 上流プロキシ: FTP ポート
			if (NetworkSettings.Proxy.FtpPort.Value == 0)
				NetworkSettings.Proxy.FtpPort.Value = NetworkSettings.Proxy.FtpPort.Default;

			// 上流プロキシ: SOCKS ポート
			if (NetworkSettings.Proxy.SocksPort.Value == 0)
				NetworkSettings.Proxy.SocksPort.Value = NetworkSettings.Proxy.SocksPort.Default;

			// プロキシ種別: 未定義の enum 値はデフォルト (SystemProxy) にリセット
			if (!Enum.IsDefined(typeof(KanColleWrapper.ProxyType), NetworkSettings.Proxy.Type.Value))
				NetworkSettings.Proxy.Type.Value = NetworkSettings.Proxy.Type.Default;
		}

		public static void Save()
		{
			#region const message

			const string message = @"設定ファイル ({0}) の保存に失敗しました。

エラーの詳細: {1}";

			#endregion

			try
			{
				Providers.Local.Save();
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format(message, Providers.LocalFilePath, ex.Message), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
				throw;
			}

			try
			{
				Providers.Roaming.Save();
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format(message, Providers.RoamingFilePath, ex.Message), "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
				throw;
			}
		}

		#endregion

		/// <summary>
		/// <typeparamref name="T"/> 型の設定オブジェクトの唯一のインスタンスを取得します。
		/// </summary>
		public static T Instance<T>() where T : SettingsHost, new()
		{
			SettingsHost host;
			return instances.TryGetValue(typeof(T), out host) ? (T)host : new T();
		}
	}
}
