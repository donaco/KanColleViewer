using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Mvvm;

namespace Grabacr07.KanColleViewer.Infrastructure.Mvvm
{
	/// <summary>
	/// <see cref="CommunityToolkit.Mvvm.ComponentModel.ObservableObject"/> を基底クラスとし、
	/// <see cref="IDisposableHolder"/> を実装した ViewModel 基底クラスです。
	/// <see cref="Livet.ViewModel"/> の代替として使用します。
	/// </summary>
	public abstract class ViewModelBase : ObservableObject, IDisposableHolder
	{
		private readonly CompositeDisposable _compositeDisposable = new CompositeDisposable();

		/// <summary>
		/// このインスタンスのライフタイムに紐付けられた <see cref="CompositeDisposable"/> を取得します。
		/// </summary>
		public CompositeDisposable CompositeDisposable => this._compositeDisposable;

		ICollection<IDisposable> IDisposableHolder.CompositeDisposable => this._compositeDisposable;

		/// <summary>
		/// プロパティ変更通知を発火します。
		/// </summary>
		protected new void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			base.OnPropertyChanged(propertyName);
		}

		/// <summary>
		/// Livet 互換: <see cref="OnPropertyChanged(string)"/> の別名です。
		/// </summary>
		protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
		{
			base.OnPropertyChanged(propertyName);
		}

		/// <summary>
		/// UI スレッドでアクションを非同期実行します。
		/// </summary>
		protected void InvokeOnUIDispatcher(Action action)
		{
			System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing) this._compositeDisposable.Dispose();
		}

		public void Dispose()
		{
			this.Dispose(true);
			GC.SuppressFinalize(this);
		}
	}
}
