// MetroTrilithon.Serialization の内製化 (Phase 1)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xaml;

namespace MetroTrilithon.Serialization
{
    public interface ISerializationProvider
    {
        bool IsLoaded { get; }
        void Save();
        void Load();
        event EventHandler Reloaded;
        void SetValue<T>(string key, T value);
        bool TryGetValue<T>(string key, out T value);
        bool RemoveValue(string key);
    }

    public class ValueChangedEventArgs<T> : EventArgs
    {
        public T OldValue { get; }
        public T NewValue { get; }
        public ValueChangedEventArgs(T oldValue, T newValue) { this.OldValue = oldValue; this.NewValue = newValue; }
    }

    [DebuggerDisplay("Value={Value}, Key={Key}, Default={Default}")]
    public abstract class SerializablePropertyBase<T> : INotifyPropertyChanged
    {
        private T _value;
        private bool _cached;

        public string Key { get; }
        public ISerializationProvider Provider { get; }
        public bool AutoSave { get; set; }
        public T Default { get; }

        public virtual T Value
        {
            get
            {
                if (this._cached) return this._value;
                if (!this.Provider.IsLoaded) this.Provider.Load();
                object obj;
                if (this.Provider.TryGetValue(this.Key, out obj))
                {
                    this._value = this.DeserializeCore(obj);
                    this._cached = true;
                }
                else
                {
                    this._value = this.Default;
                }
                return this._cached ? this._value : this.Default;
            }
            set
            {
                if (this._cached && Equals(this._value, value)) return;
                if (!this.Provider.IsLoaded) this.Provider.Load();
                var old = this._value;
                this._value = value;
                this._cached = true;
                this.Provider.SetValue(this.Key, this.SerializeCore(value));
                this.OnValueChanged(old, value);
                if (this.AutoSave) this.Provider.Save();
            }
        }

        protected SerializablePropertyBase(string key, ISerializationProvider provider) : this(key, provider, default(T)) { }

        protected SerializablePropertyBase(string key, ISerializationProvider provider, T defaultValue)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            this.Key = key;
            this.Provider = provider;
            this.Default = defaultValue;
            this.Provider.Reloaded += (sender, args) =>
            {
                if (this._cached)
                {
                    this._cached = false;
                    var oldValue = this._value;
                    var newValue = this.Value;
                    if (!Equals(oldValue, newValue)) this.OnValueChanged(oldValue, newValue);
                }
                else
                {
                    this.OnValueChanged(default(T), this.Value);
                }
            };
        }

        protected virtual object SerializeCore(T value) => value;
        protected virtual T DeserializeCore(object value) => (T)value;

        public virtual IDisposable Subscribe(Action<T> listener)
        {
            listener(this.Value);
            return new ValueChangedEventListener(this, listener);
        }

        public virtual void Reset()
        {
            if (!this.Provider.IsLoaded) this.Provider.Load();
            object old;
            if (this.Provider.TryGetValue(this.Key, out old))
            {
                if (this.Provider.RemoveValue(this.Key))
                {
                    this._value = default(T);
                    this._cached = false;
                    this.OnValueChanged(this.DeserializeCore(old), this.Default);
                    if (this.AutoSave) this.Provider.Save();
                }
            }
        }

        public event EventHandler<ValueChangedEventArgs<T>> ValueChanged;

        protected virtual void OnValueChanged(T oldValue, T newValue)
            => this.ValueChanged?.Invoke(this, new ValueChangedEventArgs<T>(oldValue, newValue));

        private readonly Dictionary<PropertyChangedEventHandler, EventHandler<ValueChangedEventArgs<T>>> _handlers
            = new Dictionary<PropertyChangedEventHandler, EventHandler<ValueChangedEventArgs<T>>>();

        event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
        {
            add { this.ValueChanged += (this._handlers[value] = (sender, args) => value(sender, new PropertyChangedEventArgs(nameof(this.Value)))); }
            remove
            {
                EventHandler<ValueChangedEventArgs<T>> handler;
                if (this._handlers.TryGetValue(value, out handler))
                {
                    this.ValueChanged -= handler;
                    this._handlers.Remove(value);
                }
            }
        }

        public static implicit operator T(SerializablePropertyBase<T> property) => property.Value;

        private class ValueChangedEventListener : IDisposable
        {
            private readonly Action<T> _listener;
            private readonly SerializablePropertyBase<T> _source;
            public ValueChangedEventListener(SerializablePropertyBase<T> property, Action<T> listener)
            {
                this._listener = listener;
                this._source = property;
                this._source.ValueChanged += this.HandleValueChanged;
            }
            private void HandleValueChanged(object sender, ValueChangedEventArgs<T> args) => this._listener(args.NewValue);
            public void Dispose() => this._source.ValueChanged -= this.HandleValueChanged;
        }
    }

    public sealed class SerializableProperty<T> : SerializablePropertyBase<T>
    {
        public SerializableProperty(string key) : this(key, default(T)) { }
        public SerializableProperty(string key, T defaultValue) : base(key, ApplicationSettingsProvider.Default, defaultValue) { }
        public SerializableProperty(string key, ISerializationProvider provider) : base(key, provider) { }
        public SerializableProperty(string key, ISerializationProvider provider, T defaultValue) : base(key, provider, defaultValue) { }
    }

    public class ApplicationSettingsProvider : ApplicationSettingsBase, ISerializationProvider
    {
        public static ISerializationProvider Default { get; } = new ApplicationSettingsProvider(typeof(ApplicationSettingsProvider).FullName + "." + nameof(Default));

        public bool IsLoaded { get; private set; }

        [UserScopedSetting, EditorBrowsable(EditorBrowsableState.Never)]
        public object __Infrastructure { get; }

        public ApplicationSettingsProvider() { }
        public ApplicationSettingsProvider(string settingsKey) : base(settingsKey) { }

        public void SetValue<T>(string key, T value)
        {
            this.AddProperty(key, typeof(T));
            this[key] = value;
        }

        public bool TryGetValue<T>(string key, out T value)
        {
            this.AddProperty(key, typeof(T));
            try { value = (T)this[key]; return true; }
            catch { value = default(T); return false; }
        }

        public bool RemoveValue(string key) => this.RemoveProperty(key);

        private void AddProperty(string key, Type type)
        {
            if (this.Properties.OfType<SettingsProperty>().All(x => x.Name != key))
            {
                var property = new SettingsProperty(key)
                {
                    PropertyType = type,
                    Provider = this.Providers.OfType<SettingsProvider>().FirstOrDefault(),
                    SerializeAs = SettingsSerializeAs.Xml,
                };
                property.Attributes.Add(typeof(UserScopedSettingAttribute), new UserScopedSettingAttribute());
                this.Properties.Add(property);
                this.Reload();
            }
        }

        private bool RemoveProperty(string key)
        {
            if (this.Properties.OfType<SettingsProperty>().All(x => x.Name != key)) return false;
            this.Properties.Remove(key);
            this.Reload();
            return true;
        }

        void ISerializationProvider.Load()
        {
            this.Reload();
            this.IsLoaded = true;
        }

        void ISerializationProvider.Save() => this.Save();

        event EventHandler ISerializationProvider.Reloaded { add { } remove { } }
    }

    public class FileSettingsProvider : ISerializationProvider
    {
        private readonly string _path;
        private readonly object _sync = new object();
        private SortedDictionary<string, object> _settings = new SortedDictionary<string, object>();

        public bool IsLoaded { get; private set; }

        public FileSettingsProvider(string path) { this._path = path; }

        public void SetValue<T>(string key, T value)
        {
            lock (this._sync) this._settings[key] = value;
        }

        public bool TryGetValue<T>(string key, out T value)
        {
            lock (this._sync)
            {
                object obj;
                if (this._settings.TryGetValue(key, out obj) && obj is T)
                {
                    value = (T)obj; return true;
                }
            }
            value = default(T); return false;
        }

        public bool RemoveValue(string key)
        {
            lock (this._sync) return this._settings.Remove(key);
        }

        public void Save()
        {
            if (this._settings.Count == 0) return;
            var dir = Path.GetDirectoryName(this._path);
            if (dir == null) throw new DirectoryNotFoundException();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            lock (this._sync)
            {
                using (var stream = new FileStream(this._path, FileMode.Create, FileAccess.ReadWrite))
                    XamlServices.Save(stream, this._settings);
            }
        }

        public void Load()
        {
            if (File.Exists(this._path))
            {
                using (var stream = new FileStream(this._path, FileMode.Open, FileAccess.Read))
                {
                    lock (this._sync)
                    {
                        var source = XamlServices.Load(stream) as IDictionary<string, object>;
                        this._settings = source == null
                            ? new SortedDictionary<string, object>()
                            : new SortedDictionary<string, object>(source);
                    }
                }
            }
            else
            {
                lock (this._sync) this._settings = new SortedDictionary<string, object>();
            }
            this.IsLoaded = true;
        }

        event EventHandler ISerializationProvider.Reloaded { add { } remove { } }
    }
}
