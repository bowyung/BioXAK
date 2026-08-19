using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace BioSAK.Localization
{
    /// <summary>
    /// XAML markup extension: Text="{loc:L Some_Key}".
    /// Produces a OneWay binding onto LocalizationManager's indexer, so the whole
    /// UI updates live the moment the language is switched - no restart needed.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class LExtension : MarkupExtension
    {
        public LExtension() { }
        public LExtension(string key) { Key = key; }

        [ConstructorArgument("key")]
        public string Key { get; set; } = string.Empty;

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key)) return string.Empty;

            var binding = new Binding($"[{Key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay,
                FallbackValue = Key
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
