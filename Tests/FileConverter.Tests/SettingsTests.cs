using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileConverter.Tests
{
    [TestClass]
    public class SettingsTests
    {
        [TestMethod]
        public void SerializableConversionPresets_NullArray_IsIgnored()
        {
            var settings = new Settings();

            settings.SerializableConversionPresets = null;

            Assert.IsNotNull(settings.ConversionPresets);
            Assert.AreEqual(0, settings.ConversionPresets.Count);
        }

        [TestMethod]
        public void SerializableConversionPresets_NullEntry_IsSkipped()
        {
            var valid = new ConversionPreset("Valid", OutputType.Gif, "jpg");
            var settings = new Settings();

            settings.SerializableConversionPresets = new[] { null, valid };

            Assert.AreEqual(1, settings.ConversionPresets.Count);
            Assert.AreSame(valid, settings.ConversionPresets[0]);
        }

        [TestMethod]
        public void Clean_NullPreset_RemovesItWithoutThrowing()
        {
            var settings = new Settings();
            settings.ConversionPresets.Add(null);
            settings.ConversionPresets.Add(new ConversionPreset("Valid", OutputType.Gif, "jpg"));

            settings.Clean();

            Assert.AreEqual(1, settings.ConversionPresets.Count);
            Assert.IsNotNull(settings.ConversionPresets[0]);
        }

        [TestMethod]
        public void Merge_NullEntries_DoNotPreventValidPresetMerge()
        {
            var target = new Settings();
            target.ConversionPresets.Add(null);

            var source = new Settings();
            source.ConversionPresets.Add(null);
            source.ConversionPresets.Add(new ConversionPreset("Valid", OutputType.Gif, "jpg"));

            target.Merge(source);

            Assert.AreEqual(2, target.ConversionPresets.Count);
            Assert.IsTrue(target.ConversionPresets.ExistsForTest("Valid"));
        }
    }

    internal static class SettingsTestExtensions
    {
        public static bool ExistsForTest(this System.Collections.ObjectModel.ObservableCollection<ConversionPreset> presets, string fullName)
        {
            foreach (ConversionPreset preset in presets)
            {
                if (preset != null && preset.FullName == fullName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
