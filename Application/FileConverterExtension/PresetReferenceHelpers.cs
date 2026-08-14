// <copyright file="PresetReferenceHelpers.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverterExtension
{
    using System;
    using System.IO;

    /// <summary>
    /// Provides fail-safe loading and matching for preset references used by Explorer.
    /// </summary>
    public static class PresetReferenceHelpers
    {
        public static bool SupportsExtension(PresetReference preset, string extension)
        {
            if (preset == null || preset.InputTypes == null || string.IsNullOrEmpty(extension))
            {
                return false;
            }

            for (int index = 0; index < preset.InputTypes.Length; index++)
            {
                string inputType = preset.InputTypes[index];
                if (string.Equals(inputType, extension, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool AnySupportsExtension(PresetReference[] presets, string extension)
        {
            if (presets == null || string.IsNullOrEmpty(extension))
            {
                return false;
            }

            for (int index = 0; index < presets.Length; index++)
            {
                if (SupportsExtension(presets[index], extension))
                {
                    return true;
                }
            }

            return false;
        }

        public static PresetReference[] Load(string userSettingsPath, string defaultSettingsPath)
        {
            if (TryLoad(userSettingsPath, out PresetReference[] presets))
            {
                return Normalize(presets);
            }

            if (TryLoad(defaultSettingsPath, out presets))
            {
                return Normalize(presets);
            }

            return Array.Empty<PresetReference>();
        }

        private static bool TryLoad(string path, out PresetReference[] presets)
        {
            presets = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                XmlHelpers.LoadFromFile("Settings", path, out presets);
                return true;
            }
            catch
            {
                presets = null;
                return false;
            }
        }

        private static PresetReference[] Normalize(PresetReference[] presets)
        {
            if (presets == null)
            {
                return Array.Empty<PresetReference>();
            }

            for (int index = 0; index < presets.Length; index++)
            {
                presets[index]?.Normalize();
            }

            return presets;
        }
    }
}
