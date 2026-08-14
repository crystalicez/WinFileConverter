# Preset InputTypes Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make empty or malformed preset input-type data safe across settings serialization and the Explorer shell extension so one bad preset can never suppress the whole File Converter context menu.

**Architecture:** Preserve the current XML schema and normalize `ConversionPreset.InputTypes` to a non-null in-memory collection at the main application model boundary. Add a small shell-side `PresetReferenceHelpers` boundary that safely loads preset references and evaluates extension compatibility without COM/Explorer state, then have `FileConverterExtension` delegate to it. Keep explicit defensive checks at the shell boundary because Explorer reads settings files independently of the main app.

**Tech Stack:** C# / .NET Framework 4.8, WPF, SharpShell 2.7.2, XML serialization, MSTest 4.3.3, Microsoft.NET.Test.Sdk 18.8.1, Visual Studio/MSBuild x64.

## Global Constraints

- Do not change the existing XML schema or require a settings migration.
- Do not reject presets that intentionally have zero compatible input types.
- Do not redesign the Windows 11 modern context menu integration.
- Do not perform unrelated refactoring of preset or settings code.
- Preserve `Settings.Version = 4`.
- A zero-input preset is valid and matches no input files.
- A malformed preset must affect only itself; valid presets in the same settings file must remain usable.

---

## File Structure

- Create `Tests/FileConverter.Tests/FileConverter.Tests.csproj` — .NET Framework 4.8 MSTest regression project referencing both production projects.
- Create `Tests/FileConverter.Tests/ConversionPresetTests.cs` — main model null/empty input-type and XML round-trip tests.
- Create `Tests/FileConverter.Tests/SettingsTests.cs` — settings collection malformed-entry tests.
- Create `Tests/FileConverter.Tests/PresetReferenceHelpersTests.cs` — shell-side loading and compatibility tests without Explorer/COM.
- Modify `FileConverter.sln` — add the regression test project and x64 configuration mappings.
- Modify `Application/FileConverter/ConversionPreset/ConversionPreset.cs` — establish and maintain the non-null `InputTypes` invariant.
- Modify `Application/FileConverter/Settings.cs` — tolerate null serialized arrays and null preset entries.
- Create `Application/FileConverterExtension/PresetReferenceHelpers.cs` — pure shell-boundary loader and compatibility helper.
- Modify `Application/FileConverterExtension/ConversionPresetReference.cs` — make `InputTypes` non-null after construction/deserialization.
- Modify `Application/FileConverterExtension/FileConverterExtension.cs` — use safe loading/matching in `CanShowMenu()` and `RefreshPresetList()`.
- Modify `Application/FileConverterExtension/FileConverterExtension.csproj` — compile the new helper.

---

### Task 1: Add regression test infrastructure and capture the main-model failures

**Files:**
- Create: `Tests/FileConverter.Tests/FileConverter.Tests.csproj`
- Create: `Tests/FileConverter.Tests/ConversionPresetTests.cs`
- Create: `Tests/FileConverter.Tests/SettingsTests.cs`
- Modify: `FileConverter.sln`

**Interfaces:**
- Consumes: public `FileConverter.ConversionPreset`, `FileConverter.Settings`, and `FileConverter.XmlHelpers`.
- Produces: x64 .NET Framework 4.8 MSTest project `FileConverter.Tests` used by later tasks.

- [ ] **Step 1: Create the x64 .NET Framework 4.8 MSTest project**

Create `Tests/FileConverter.Tests/FileConverter.Tests.csproj` as an old-style project so it uses the same full-framework MSBuild toolchain as the existing solution:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">x64</Platform>
    <ProjectGuid>{8B2F3160-5D9D-47EF-AF1D-5F226BC9362F}</ProjectGuid>
    <OutputType>Library</OutputType>
    <RootNamespace>FileConverter.Tests</RootNamespace>
    <AssemblyName>FileConverter.Tests</AssemblyName>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    <FileAlignment>512</FileAlignment>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|x64' ">
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>bin\x64\Debug\</OutputPath>
    <DefineConstants>TRACE;DEBUG</DefineConstants>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|x64' ">
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>bin\x64\Release\</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System" />
    <Reference Include="System.Core" />
    <Reference Include="System.Xml" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="ConversionPresetTests.cs" />
    <Compile Include="SettingsTests.cs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Application\FileConverter\FileConverter.csproj">
      <Project>{D27A76D2-43E4-43CC-9DA3-334B0B46F4E5}</Project>
      <Name>FileConverter</Name>
    </ProjectReference>
    <ProjectReference Include="..\..\Application\FileConverterExtension\FileConverterExtension.csproj">
      <Project>{0C44CA69-42D6-4357-BDFD-83069D1ABA2F}</Project>
      <Name>FileConverterExtension</Name>
    </ProjectReference>
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="MSTest.TestAdapter" Version="4.3.3" />
    <PackageReference Include="MSTest.TestFramework" Version="4.3.3" />
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

Add the project to `FileConverter.sln` with project GUID `{8B2F3160-5D9D-47EF-AF1D-5F226BC9362F}` and add `Debug|x64` / `Release|x64` `ActiveCfg` and `Build.0` mappings.

- [ ] **Step 2: Write failing `ConversionPreset` invariant tests**

Create `Tests/FileConverter.Tests/ConversionPresetTests.cs`:

```csharp
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
        public void Constructor_WhenInputTypesArrayIsNull_ProducesEmptyCollection()
        {
            var preset = new ConversionPreset("Empty", OutputType.Gif, (string[])null);

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
```

- [ ] **Step 3: Write failing settings collection tests**

Create `Tests/FileConverter.Tests/SettingsTests.cs`:

```csharp
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
```

The `Merge` assertion deliberately expects the pre-existing null entry to remain because `Merge()` should skip malformed entries rather than silently perform cleanup; `Clean()` owns cleanup before save.

- [ ] **Step 4: Restore/build and verify the tests fail for production behavior, not project setup**

On Windows with Visual Studio 2022 or Build Tools installed:

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
$vstest = & $vswhere -latest -products * -find "Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" | Select-Object -First 1

& $msbuild FileConverter.sln /restore /t:Build /p:Configuration=Debug /p:Platform=x64
& $vstest Tests\FileConverter.Tests\bin\x64\Debug\FileConverter.Tests.dll /Platform:x64
```

Expected: test assembly builds; tests fail because current production code dereferences null `InputTypes`, null serialized arrays, or null preset entries. Do not accept missing source files or test-discovery failures as the red phase.

- [ ] **Step 5: Commit the failing regression tests**

```bash
git add FileConverter.sln Tests/FileConverter.Tests
git commit -m "test: cover malformed preset input types"
```

---

### Task 2: Enforce the non-null input-type invariant in the main application

**Files:**
- Modify: `Application/FileConverter/ConversionPreset/ConversionPreset.cs`
- Modify: `Application/FileConverter/Settings.cs`
- Test: `Tests/FileConverter.Tests/ConversionPresetTests.cs`
- Test: `Tests/FileConverter.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: existing `ConversionPreset.InputTypes`, `IXmlSerializable.OnDeserializationComplete()`, and `Settings.SerializableConversionPresets` serialization hooks.
- Produces: `ConversionPreset.InputTypes` is always a non-null `List<string>` in memory; settings collection operations tolerate null preset entries.

- [ ] **Step 1: Establish the field invariant with one normalization method**

In `ConversionPreset.cs`, initialize the field and add:

```csharp
private List<string> inputTypes = new List<string>();

private void NormalizeInputTypes()
{
    if (this.inputTypes == null)
    {
        this.inputTypes = new List<string>();
        return;
    }

    for (int index = this.inputTypes.Count - 1; index >= 0; index--)
    {
        string inputType = this.inputTypes[index];
        if (string.IsNullOrEmpty(inputType))
        {
            this.inputTypes.RemoveAt(index);
            continue;
        }

        this.inputTypes[index] = inputType.ToLowerInvariant();
    }
}
```

Keep normalization narrow: remove null/empty entries and lower-case non-empty extensions. Do not trim, deduplicate, or otherwise change semantics.

- [ ] **Step 2: Normalize construction, assignment, and deserialization**

In the constructor taking `params string[] inputTypes`, replace unconditional `AddRange` with:

```csharp
List<string> inputTypeList = new List<string>();
if (inputTypes != null)
{
    inputTypeList.AddRange(inputTypes);
}

this.InputTypes = inputTypeList;
```

Replace the `InputTypes` setter body with:

```csharp
set
{
    this.inputTypes = value == null ? new List<string>() : new List<string>(value);
    this.NormalizeInputTypes();
    this.OnPropertyChanged();
}
```

Replace the manual lower-casing loop in `OnDeserializationComplete()` with:

```csharp
this.NormalizeInputTypes();
this.CoerceInputTypes();
```

At the start of `AddInputType`, add:

```csharp
if (string.IsNullOrEmpty(inputType))
{
    return;
}

inputType = inputType.ToLowerInvariant();
```

`RemoveInputType()` and `CoerceInputTypes()` may rely on the invariant after this change; do not scatter redundant null checks through every call site.

- [ ] **Step 3: Harden `Settings` collection boundaries**

Change `Settings.Clean()` to remove null entries while cleaning valid presets:

```csharp
for (int index = this.ConversionPresets.Count - 1; index >= 0; index--)
{
    ConversionPreset preset = this.ConversionPresets[index];
    if (preset == null)
    {
        this.ConversionPresets.RemoveAt(index);
        continue;
    }

    preset.Clean();
}
```

Change `SerializableConversionPresets` setter to:

```csharp
set
{
    if (value == null)
    {
        return;
    }

    for (int index = 0; index < value.Length; index++)
    {
        ConversionPreset preset = value[index];
        if (preset != null)
        {
            this.ConversionPresets.Add(preset);
        }
    }
}
```

In `Merge(Settings settings)`, use these exact guards:

```csharp
for (int index = 0; index < settings.conversionPresets.Count; index++)
{
    ConversionPreset conversionPreset = settings.conversionPresets[index];
    if (conversionPreset == null)
    {
        continue;
    }

    if (this.conversionPresets.Any(match => match != null && match.FullName == conversionPreset.FullName))
    {
        continue;
    }

    this.conversionPresets.Add(conversionPreset);
}
```

In `OnDeserializationComplete()`, iterate backwards and remove null presets before invoking their completion hooks:

```csharp
for (int index = this.ConversionPresets.Count - 1; index >= 0; index--)
{
    ConversionPreset preset = this.ConversionPresets[index];
    if (preset == null)
    {
        this.ConversionPresets.RemoveAt(index);
        continue;
    }

    preset.OnDeserializationComplete();
}
```

- [ ] **Step 4: Run model/settings tests and verify they pass**

```powershell
& $msbuild FileConverter.sln /restore /t:Build /p:Configuration=Debug /p:Platform=x64
& $vstest Tests\FileConverter.Tests\bin\x64\Debug\FileConverter.Tests.dll /Platform:x64
```

Expected: all Task 1 tests pass.

- [ ] **Step 5: Commit main application hardening**

```bash
git add Application/FileConverter/ConversionPreset/ConversionPreset.cs Application/FileConverter/Settings.cs
git commit -m "fix: normalize preset input types"
```

---

### Task 3: Add a testable shell boundary and harden Explorer menu evaluation

**Files:**
- Create: `Application/FileConverterExtension/PresetReferenceHelpers.cs`
- Modify: `Application/FileConverterExtension/ConversionPresetReference.cs`
- Modify: `Application/FileConverterExtension/FileConverterExtension.cs`
- Modify: `Application/FileConverterExtension/FileConverterExtension.csproj`
- Create: `Tests/FileConverter.Tests/PresetReferenceHelpersTests.cs`
- Modify: `Tests/FileConverter.Tests/FileConverter.Tests.csproj`

**Interfaces:**
- Consumes: `PresetReference[]`, user/default settings paths, and `FileConverterExtension.XmlHelpers.LoadFromFile<T>()`.
- Produces: `PresetReferenceHelpers.Load(string userSettingsFilePath, string defaultSettingsFilePath) -> PresetReference[]`, `PresetReferenceHelpers.SupportsExtension(PresetReference preset, string extension) -> bool`, and `PresetReferenceHelpers.AnySupportsExtension(PresetReference[] presets, string extension) -> bool`.

- [ ] **Step 1: Write failing shell-boundary tests**

Create `Tests/FileConverter.Tests/PresetReferenceHelpersTests.cs`:

```csharp
using System;
using System.IO;
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
                var presets = Shell.PresetReferenceHelpers.Load(
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

                var presets = Shell.PresetReferenceHelpers.Load(user, defaults);

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

                var presets = Shell.PresetReferenceHelpers.Load(user, Path.Combine(root, "missing-default.xml"));

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
            var preset = (Shell.PresetReference)Activator.CreateInstance(typeof(Shell.PresetReference), true);
            preset.InputTypes = inputTypes;
            return preset;
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
```

Add this compile item to `Tests/FileConverter.Tests/FileConverter.Tests.csproj` only in this task:

```xml
<Compile Include="PresetReferenceHelpersTests.cs" />
```

Run build. Expected: compilation fails because `PresetReferenceHelpers` does not exist yet; this is the intentional red phase.

- [ ] **Step 2: Make `PresetReference.InputTypes` non-null by construction and assignment**

In `ConversionPresetReference.cs`, add `using System;`, replace the auto-property with:

```csharp
private string[] inputTypes = Array.Empty<string>();

[XmlElement]
public string[] InputTypes
{
    get => this.inputTypes;
    set => this.inputTypes = value ?? Array.Empty<string>();
}
```

Keep the private parameterless constructor and the existing `[XmlElement]` contract unchanged.

- [ ] **Step 3: Implement the focused shell helper**

Create `Application/FileConverterExtension/PresetReferenceHelpers.cs`:

```csharp
namespace FileConverterExtension
{
    using System;
    using System.IO;

    public static class PresetReferenceHelpers
    {
        public static bool SupportsExtension(PresetReference preset, string extension)
        {
            if (preset == null || string.IsNullOrEmpty(extension))
            {
                return false;
            }

            string[] inputTypes = preset.InputTypes ?? Array.Empty<string>();
            return Array.IndexOf(inputTypes, extension) >= 0;
        }

        public static bool AnySupportsExtension(PresetReference[] presets, string extension)
        {
            if (presets == null)
            {
                return false;
            }

            foreach (PresetReference preset in presets)
            {
                if (SupportsExtension(preset, extension))
                {
                    return true;
                }
            }

            return false;
        }

        public static PresetReference[] Load(string userSettingsFilePath, string defaultSettingsFilePath)
        {
            PresetReference[] presets;
            if (TryLoad(userSettingsFilePath, out presets))
            {
                return presets ?? Array.Empty<PresetReference>();
            }

            if (TryLoad(defaultSettingsFilePath, out presets))
            {
                return presets ?? Array.Empty<PresetReference>();
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
                return false;
            }
        }
    }
}
```

Add `<Compile Include="PresetReferenceHelpers.cs" />` to `FileConverterExtension.csproj`.

- [ ] **Step 4: Delegate shell matching to the safe helper**

Replace the inner preset loop in `CanShowMenu()` with one helper call per selected extension:

```csharp
PresetReference[] presets = this.PresetReferences;
foreach (string extension in this.extensionCache)
{
    if (PresetReferenceHelpers.AnySupportsExtension(presets, extension))
    {
        return true;
    }
}
```

In `RefreshPresetList()`, load through `this.PresetReferences` and use `SupportsExtension`:

```csharp
PresetReference[] presets = this.PresetReferences;
this.menuEntries.Clear();
foreach (string extension in this.extensionCache)
{
    foreach (PresetReference presetReference in presets)
    {
        if (!PresetReferenceHelpers.SupportsExtension(presetReference, extension))
        {
            continue;
        }

        MenuEntry menuEntry = this.menuEntries.Find(
            entry => entry.PresetReference.FullName == presetReference.FullName);
        if (menuEntry == null)
        {
            menuEntry = new MenuEntry(presetReference);
            this.menuEntries.Add(menuEntry);
        }

        menuEntry.ExtensionRefCount++;
    }
}
```

- [ ] **Step 5: Make configured-path resolution and settings loading fail-safe**

Replace `LoadExtensionSettingsIfNecessary()` with:

```csharp
private void LoadExtensionSettingsIfNecessary()
{
    if (this.presetReferences != null)
    {
        return;
    }

    string userSettingsFilePath = null;
    string defaultSettingsFilePath = null;

    try
    {
        userSettingsFilePath = PathHelpers.UserSettingsFilePath;
    }
    catch
    {
        // Explorer extensions must fail closed when settings paths cannot be resolved.
    }

    try
    {
        defaultSettingsFilePath = PathHelpers.DefaultSettingsFilePath;
    }
    catch
    {
        // Explorer extensions must fail closed when settings paths cannot be resolved.
    }

    this.presetReferences = PresetReferenceHelpers.Load(
        userSettingsFilePath,
        defaultSettingsFilePath);
}
```

This keeps `null` as the lazy-load sentinel before the first attempt and guarantees a non-null array afterwards.

- [ ] **Step 6: Run shell regression tests and complete test assembly**

```powershell
& $msbuild FileConverter.sln /restore /t:Build /p:Configuration=Debug /p:Platform=x64
& $vstest Tests\FileConverter.Tests\bin\x64\Debug\FileConverter.Tests.dll /Platform:x64
```

Expected: all tests pass, including malformed-before-valid `.jpg` matching, corrupt-user fallback, and no-input preset handling.

- [ ] **Step 7: Commit shell hardening**

```bash
git add Application/FileConverterExtension Tests/FileConverter.Tests/PresetReferenceHelpersTests.cs Tests/FileConverter.Tests/FileConverter.Tests.csproj
git commit -m "fix: harden shell preset loading"
```

---

### Task 4: Full verification and Windows Explorer regression check

**Files:**
- Verify: `Application/FileConverter/ConversionPreset/ConversionPreset.cs`
- Verify: `Application/FileConverter/Settings.cs`
- Verify: `Application/FileConverterExtension/ConversionPresetReference.cs`
- Verify: `Application/FileConverterExtension/PresetReferenceHelpers.cs`
- Verify: `Application/FileConverterExtension/FileConverterExtension.cs`
- Verify: `Tests/FileConverter.Tests/*`

**Interfaces:**
- Consumes: completed production and test changes from Tasks 1–3.
- Produces: evidence that the solution builds, tests pass, XML compatibility is unchanged, and the original Explorer failure no longer reproduces.

- [ ] **Step 1: Build Debug x64 and Release x64 from a clean state**

```powershell
& $msbuild FileConverter.sln /restore /t:Clean,Build /p:Configuration=Debug /p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "Debug x64 build failed" }

& $msbuild FileConverter.sln /restore /t:Clean,Build /p:Configuration=Release /p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "Release x64 build failed" }
```

Expected: both builds exit with code 0.

- [ ] **Step 2: Run all regression tests against Debug x64**

```powershell
& $vstest Tests\FileConverter.Tests\bin\x64\Debug\FileConverter.Tests.dll /Platform:x64
if ($LASTEXITCODE -ne 0) { throw "Regression tests failed" }
```

Expected: all tests pass with zero failures.

- [ ] **Step 3: Verify schema/version compatibility in the diff**

```bash
git diff integration...HEAD -- Application/FileConverter/Settings.cs Application/FileConverter/ConversionPreset/ConversionPreset.cs Application/FileConverterExtension/ConversionPresetReference.cs
```

Confirm:

- `Settings.Version` remains `4`.
- `InputTypes` retains `[XmlElement]` in both model representations.
- No wrapper element or settings migration was added.
- Empty input lists remain representable.

- [ ] **Step 4: Reproduce the original Windows 11 case with a zero-input preset**

1. Install/register the hardened build on the Windows 11 test machine.
2. In File Converter settings, create `Scale 25%/To Gif 15fps` and leave all input types unchecked.
3. Save settings and confirm the preset may serialize with no `InputTypes` elements.
4. Restart Explorer.
5. Right-click a `.jpg` supported by another valid preset and choose **Show more options**.
6. Verify **File Converter** is present and valid `.jpg` presets are available.
7. Verify the zero-input preset is not offered for the `.jpg`.

- [ ] **Step 5: Verify SharpShell no longer reports the original exception**

With the diagnostic file logging used during root-cause analysis enabled, trigger the menu once and run:

```powershell
Get-Content "C:\Temp\SharpShell.log" | Select-String "ArgumentNullException|CanShowMenu|Query Context Menu"
```

Expected: `Query Context Menu` may appear, but no `ArgumentNullException` from `Enumerable.Contains` / `FileConverterExtension.CanShowMenu()` appears.

- [ ] **Step 6: Final diff review before merge or PR**

```bash
git status --short
git diff --check integration...HEAD
git log --oneline integration..HEAD
```

Expected: clean working tree, no whitespace errors, and commits limited to tests, model normalization, shell hardening, and the approved design/plan documentation.
