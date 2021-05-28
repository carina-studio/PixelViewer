using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Configuration
{
	/// <summary>
	/// Base implementation of application settings.
	/// </summary>
	class Settings : INotifyPropertyChanged
	{
		/// <summary>
		/// Select language automatically, type is <see cref="bool"/>.
		/// </summary>
		public const string AutoSelectLanguage = nameof(AutoSelectLanguage);
		/// <summary>
		/// Use dark interface mode, type is <see cref="bool"/>.
		/// </summary>
		public const string DarkMode = nameof(DarkMode);
		/// <summary>
		/// Default aspect ratio for image dimensions evaluation, type is <see cref="Carina.PixelViewer.Media.AspectRatio"/>.
		/// </summary>
		public const string DefaultImageDimensionsEvaluationAspectRatio = nameof(DefaultImageDimensionsEvaluationAspectRatio);
		/// <summary>
		/// Name of format of default image renderer, type is <see cref="string"/>.
		/// </summary>
		public const string DefaultImageRendererFormatName = nameof(DefaultImageRendererFormatName);
		/// <summary>
		/// Evaluate image dimensions after changing image renderer, type is <see cref="bool"/>.
		/// </summary>
		public const string EvaluateImageDimensionsAfterChangingRenderer = nameof(EvaluateImageDimensionsAfterChangingRenderer);
		/// <summary>
		/// Evaluate image dimensions after opening file, type is <see cref="bool"/>.
		/// </summary>
		public const string EvaluateImageDimensionsAfterOpeningSourceFile = nameof(EvaluateImageDimensionsAfterOpeningSourceFile);
		/// <summary>
		/// Height of main window, type is <see cref="int"/>.
		/// </summary>
		public const string MainWindowHeight = nameof(MainWindowHeight);
		/// <summary>
		/// State of main window, type is <see cref="Avalonia.Controls.WindowState"/>.
		/// </summary>
		public const string MainWindowState = nameof(MainWindowState);
		/// <summary>
		/// Width of main window, type is <see cref="int"/>.
		/// </summary>
		public const string MainWindowWidth = nameof(MainWindowWidth);
		/// <summary>
		/// Maximum memory usage for image rendering, type is <see cref="long"/>.
		/// </summary>
		public const string MaxRenderedImagesMemoryUsageMB = nameof(MaxRenderedImagesMemoryUsageMB);
		/// <summary>
		/// Change to default image renderer after opening file, type is <see cref="bool"/>.
		/// </summary>
		public const string UseDefaultImageRendererAfterOpeningSourceFile = nameof(UseDefaultImageRendererAfterOpeningSourceFile);
		/// <summary>
		/// YUV to RGB conversion mode, type is <see cref="Carina.PixelViewer.Media.YuvConversionMode"/>.
		/// </summary>
		public const string YuvConversionMode = nameof(YuvConversionMode);


		// Constants.
		const int Version = 1;


		// Static fields.
		static readonly Dictionary<string, object> DefaultValues = new Dictionary<string, object>();
		static readonly ILogger Logger = LogManager.GetCurrentClassLogger();


		// Fields.
		readonly Dictionary<string, object> values = new Dictionary<string, object>(DefaultValues);


		// Static initializer.
		static Settings()
		{
			DefineSetting(AutoSelectLanguage, true);
			DefineSetting(DarkMode, true);
			DefineSetting(DefaultImageDimensionsEvaluationAspectRatio, Carina.PixelViewer.Media.AspectRatio.Unknown);
			DefineSetting(DefaultImageRendererFormatName, "L8");
			DefineSetting(EvaluateImageDimensionsAfterChangingRenderer, false);
			DefineSetting(EvaluateImageDimensionsAfterOpeningSourceFile, true);
			DefineSetting(MainWindowHeight, 0);
			DefineSetting(MainWindowState, Avalonia.Controls.WindowState.Maximized);
			DefineSetting(MainWindowWidth, 0);
			DefineSetting(MaxRenderedImagesMemoryUsageMB, 1024L);
			DefineSetting(UseDefaultImageRendererAfterOpeningSourceFile, false);
			DefineSetting(YuvConversionMode, Carina.PixelViewer.Media.YuvConversionMode.NTSC);
		}


		/// <summary>
		/// Initialize new <see cref="Settings"/> instance.
		/// </summary>
		public Settings()
		{ }


		// Define setting.
		static void DefineSetting(string key, object defaultValue) => DefaultValues.Add(key, defaultValue);


		/// <summary>
		/// Get setting value as specific type.
		/// </summary>
		/// <typeparam name="T">Value type.</typeparam>
		/// <param name="key">Key of setting value.</param>
		/// <returns>Value gotten as specific type.</returns>
		public T GetValue<T>(string key)
		{
			if (this.TryGetValue<T>(key, out var value))
				return (T)((object?)value).EnsureNonNull();
			throw new InvalidOperationException($"Cannot get value of '{key}' as {typeof(T)}");
		}


		/// <summary>
		/// Load setting values from file asynchronously.
		/// </summary>
		/// <param name="fileName">Settings file name.</param>
		/// <returns>True if setting values loaded successfully.</returns>
		public async Task<bool> LoadAsync(string fileName)
		{
			// load from file
			var version = Version;
			var values = await Task.Run(() =>
			{
				try
				{
					var values = new Dictionary<string, object>(DefaultValues);
					if (!File.Exists(fileName))
						return values;
					using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
					using var jsonDocument = JsonDocument.Parse(stream);
					if (jsonDocument.RootElement.ValueKind == JsonValueKind.Object)
					{
						foreach (var keyValue in jsonDocument.RootElement.EnumerateObject())
						{
							if (keyValue.Name == "Version")
							{
								if (keyValue.Value.TryGetInt32(out var intValue))
									version = intValue;
								continue;
							}
							if (!values.ContainsKey(keyValue.Name))
							{
								Logger.Warn($"Unknown key '{keyValue.Name}' in '{fileName}'");
								continue;
							}
							values[keyValue.Name] = keyValue.Value.ValueKind switch
							{
								JsonValueKind.False => false,
								JsonValueKind.Number => keyValue.Value.Let((it) =>
								{
									if (it.TryGetInt32(out var intValue))
										return (object)intValue;
									if (it.TryGetInt64(out var longValue))
										return (object)longValue;
									if (it.TryGetDouble(out var doubleValue))
										return (object)doubleValue;
									throw new Exception($"Unsupported number {it} for '{keyValue.Name}' in '{fileName}'");
								}),
								JsonValueKind.String => keyValue.Value.GetString(),
								JsonValueKind.True => true,
								_ => throw new Exception($"Unsupported value kind {keyValue.Value.ValueKind} for '{keyValue.Name}' in '{fileName}'"),
							};
						}
					}
					else
						throw new Exception($"Incorrect JSON root element in '{fileName}'");
					return values;
				}
				catch (Exception ex)
				{
					Logger.Error(ex, $"Error occurred while loading setting values from '{fileName}'");
					return (Dictionary<string, object>?)null;
				}
			});
			if (values == null)
				return false;

			// update values
			foreach (var keyValue in values)
			{
				this.values[keyValue.Key] = keyValue.Value;
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(keyValue.Key));
			}

			// upgrade
			if (version < Version)
				this.OnUpgrade(version);

			// complete
			return true;
		}


		// Upgrade settings.
		void OnUpgrade(int oldVersion)
		{ }


		/// <summary>
		/// Raised when property changed.
		/// </summary>
		public event PropertyChangedEventHandler? PropertyChanged;


		/// <summary>
		/// Save setting values to file.
		/// </summary>
		/// <param name="fileName">File name.</param>
		public void Save(string fileName)
		{
			Dictionary<string, object> values;
			lock (this)
			{
				values = new Dictionary<string, object>(this.values);
			}
			this.Save(fileName, values);
		}


		// Save setting values.
		void Save(string fileName, IDictionary<string, object> values)
		{
			// write to memory
			using var memoryStream = new MemoryStream();
			using var jsonWriter = new Utf8JsonWriter(memoryStream, new JsonWriterOptions()
			{
				Indented = true,
			});
			jsonWriter.WriteStartObject();
			jsonWriter.WriteNumber("Version", Version);
			foreach (var keyValue in values)
			{
				var key = keyValue.Key;
				var value = keyValue.Value;
				if (!DefaultValues.TryGetValue(key, out var defaultValue) || value.Equals(defaultValue))
					continue;
				if (value is bool boolValue)
					jsonWriter.WriteBoolean(key, boolValue);
				else if (value is byte byteValue)
					jsonWriter.WriteNumber(key, byteValue);
				else if (value is short shortValue)
					jsonWriter.WriteNumber(key, shortValue);
				else if (value is int intValue)
					jsonWriter.WriteNumber(key, intValue);
				else if (value is long longValue)
					jsonWriter.WriteNumber(key, longValue);
				else if (value is float floatValue)
					jsonWriter.WriteNumber(key, floatValue);
				else if (value is double doubleValue)
					jsonWriter.WriteNumber(key, doubleValue);
				else
					jsonWriter.WriteString(key, value.ToString());
			}
			jsonWriter.WriteEndObject();
			jsonWriter.Flush();

			// write to file
			using var stream = new FileStream(fileName, FileMode.Create, FileAccess.Write);
			stream.Write(memoryStream.ToArray());
		}


		/// <summary>
		/// Save setting values to file asynchronously.
		/// </summary>
		/// <param name="fileName">File name.</param>
		public async Task SaveAsync(string fileName)
		{
			Dictionary<string, object> values;
			lock (this)
			{
				values = new Dictionary<string, object>(this.values);
			}
			await Task.Run(() => this.Save(fileName, values));
		}


		/// <summary>
		/// Get or set setting value.
		/// </summary>
		public object this[string key]
		{
			get
			{
				if (this.values.TryGetValue(key, out var value))
					return value;
				throw new ArgumentException($"Undefined key: {key}");
			}
			set
			{
				lock (this)
				{
					if (!this.values.TryGetValue(key, out var prevValue))
						throw new ArgumentException($"Undefined key: {key}");
					if (value.Equals(prevValue))
						return;
					this.values[key] = value;
					this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
				}
			}
		}


		/// <summary>
		/// Try get setting value as specific type.
		/// </summary>
		/// <typeparam name="T">Value type.</typeparam>
		/// <param name="key">Key of setting value.</param>
		/// <param name="value">Value gotten as specific type.</param>
		/// <returns>True if setting value can be gotten as specific type.</returns>
		public bool TryGetValue<T>(string key, out T? value)
		{
			value = default;
			if (!this.values.TryGetValue(key, out var rawValue))
				return false;
			var rawType = rawValue.GetType();
			var targetType = typeof(T);
			if (targetType.IsAssignableFrom(rawType))
			{
				value = (T)rawValue;
				return true;
			}
			if (targetType.IsEnum)
			{
				if (Enum.TryParse(targetType, rawValue.ToString(), out var enumValue) && enumValue != null)
				{
					value = (T)enumValue;
					return true;
				}
				return false;
			}
			if(targetType == typeof(string))
			{
				var strValue = rawValue.ToString();
				if (strValue != null)
				{
					value = (T)(object)strValue;
					return true;
				}
				return false;
			}
			if (rawValue is not IConvertible convertible)
				return false;
			try
			{
				if (targetType == typeof(bool))
					value = (T)(object)convertible.ToBoolean(null);
				else if (targetType == typeof(byte))
					value = (T)(object)convertible.ToByte(null);
				else if (targetType == typeof(short))
					value = (T)(object)convertible.ToInt16(null);
				else if (targetType == typeof(int))
					value = (T)(object)convertible.ToInt32(null);
				else if (targetType == typeof(long))
					value = (T)(object)convertible.ToInt64(null);
				else if (targetType == typeof(float))
					value = (T)(object)convertible.ToSingle(null);
				else if (targetType == typeof(double))
					value = (T)(object)convertible.ToDouble(null);
				else
					return false;
				return true;
			}
			catch
			{ }
			return false;
		}
	}
}
