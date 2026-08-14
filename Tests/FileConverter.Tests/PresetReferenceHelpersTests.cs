using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shell = FileConverterExtension;

namespace FileConverter.Tests
{
    [TestClass]
    public class PresetReferenceHelpersTests
    {
        [TestMethod]
        public void SupportsExtension_NullPreset_ReturnsFalse()
        {
            Assert.IsFalse(Shell.PresetReferenceHelpers.SupportsExtension(null, "jpg"));
        }

        [TestMethod]
        public void SupportsExtension_PresetWithoutInputTypes_ReturnsFalse()
        {
            Shell.PresetReference preset = CreatePresetReference(null);

            Assert.IsFalse(Shell.PresetReferenceHelpers.SupportsExtension(preset, "jpg"));
        }

        [TestMethod]
        public void AnySupportsExtension_MalformedPresetBeforeValidPreset_ReturnsTrue()
        {
            var presets = new[]
            {
                CreatePresetReference(null),
                CreatePresetReference(new[] { "jpg", "png" }),
            };

            Assert.IsTrue(Shell.PresetReferenceHelpers.AnySupportsExtension(presets, "jpg"));
        }

        [TestMethod]
        public void AnySupportsExtension_OnlyMalformedOrEmptyPresets_ReturnsFalse()
        {
            var presets = new[]
            {
                CreatePresetReference(null),
                CreatePresetReference(new string[0]),
            };

            Assert.IsFalse(Shell.PresetReferenceHelpers.AnySupportsExtension(presets, "jpg"));
        }

        [TestMethod]
        public void Load_MissingUserAndDefaultFiles_ReturnsEmptyArray()
        {
            string root = CreateTempDirectory();
            try
            {
                Shell.PresetReference[] presets = Shell.PresetReferenceHelpers.Load(
                    Path.Combine(root, "missing-user.xml"),
                    Path.Combine(root, "missing-default.xml"));

                Assert.IsNotNull(presets);
                Assert.AreEqual(0, presets.Length);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void Load_CorruptUserSettings_FallsBackToDefaultSettings()
        {
            string root = CreateTempDirectory();
            string user = Path.Combine(root, "Settings.user.xml");
            string defaults = Path.Combine(root, "Settings.default.xml");
            try
            {
                File.WriteAllText(user, "<Settings><broken>");
                File.WriteAllText(defaults,
                    "<Settings><ConversionPreset Name=\"Valid\"><InputTypes>jpg</InputTypes></ConversionPreset></Settings>");

                Shell.PresetReference[] presets = Shell.PresetReferenceHelpers.Load(user, defaults);

                Assert.AreEqual(1, presets.Length);
                Assert.IsTrue(Shell.PresetReferenceHelpers.SupportsExtension(presets[0], "jpg"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void Load_PresetWithoutInputTypes_LeavesSafeEmptyArray()
        {
            string root = CreateTempDirectory();
            string user = Path.Combine(root, "Settings.user.xml");
            try
            {
                File.WriteAllText(user, "<Settings><ConversionPreset Name=\"Broken\" /></Settings>");

                Shell.PresetReference[] presets = Shell.PresetReferenceHelpers.Load(
                    user,
                    Path.Combine(root, "missing-default.xml"));

                Assert.AreEqual(1, presets.Length);
                Assert.IsNotNull(presets[0].InputTypes);
                Assert.AreEqual(0, presets[0].InputTypes.Length);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static Shell.PresetReference CreatePresetReference(string[] inputTypes)
        {
            string path = Path.GetTempFileName();
            try
            {
                var xml = new StringBuilder();
                xml.Append("<Settings><ConversionPreset Name=\"Test\">");
                if (inputTypes != null)
                {
                    foreach (string inputType in inputTypes)
                    {
                        xml.Append("<InputTypes>");
                        xml.Append(System.Security.SecurityElement.Escape(inputType));
                        xml.Append("</InputTypes>");
                    }
                }

                xml.Append("</ConversionPreset></Settings>");
                File.WriteAllText(path, xml.ToString());

                Shell.XmlHelpers.LoadFromFile("Settings", path, out Shell.PresetReference[] presets);
                return presets[0];
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
