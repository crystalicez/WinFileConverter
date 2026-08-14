using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileConverter.Tests
{
    [TestClass]
    public class ConversionPresetTests
    {
        [TestMethod]
        public void InputTypes_WhenAssignedNull_BecomesEmptyCollection()
        {
            var preset = new ConversionPreset("Empty", OutputType.Gif, "jpg");

            preset.InputTypes = null;

            Assert.IsNotNull(preset.InputTypes);
            Assert.AreEqual(0, preset.InputTypes.Count);
        }

        [TestMethod]
        public void LoadPreset_WithoutInputTypes_ProducesEmptyCollection()
        {
            string path = WriteTempFile("<ConversionPreset Name=\"Empty\" OutputType=\"Gif\" />");
            try
            {
                XmlHelpers.LoadFromFile("ConversionPreset", path, out ConversionPreset preset);

                Assert.IsNotNull(preset.InputTypes);
                Assert.AreEqual(0, preset.InputTypes.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void EmptyInputTypes_RoundTrip_RemainsNonNull()
        {
            string path = Path.GetTempFileName();
            try
            {
                var preset = new ConversionPreset("Empty", OutputType.Gif, new string[0]);
                XmlHelpers.SaveToFile("ConversionPreset", path, preset);
                XmlHelpers.LoadFromFile("ConversionPreset", path, out ConversionPreset reloaded);

                Assert.IsNotNull(reloaded.InputTypes);
                Assert.AreEqual(0, reloaded.InputTypes.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void InputTypes_NullAndEmptyEntries_AreRemovedDuringNormalization()
        {
            var preset = new ConversionPreset("Image", OutputType.Gif, "jpg");

            preset.InputTypes = new List<string> { null, string.Empty, "JPG" };
            preset.OnDeserializationComplete();

            CollectionAssert.AreEqual(new[] { "jpg" }, preset.InputTypes);
        }

        private static string WriteTempFile(string contents)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(path, contents);
            return path;
        }
    }
}
