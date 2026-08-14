# Preset InputTypes Hardening Design

## Problem

A user-created preset can be serialized without any `InputTypes` elements. When the shell extension later deserializes that preset, `PresetReference.InputTypes` becomes `null`. `FileConverterExtension.CanShowMenu()` and `RefreshPresetList()` currently call `Contains()` on that value without validation, causing an exception inside Explorer. SharpShell catches the exception and returns a failure from `QueryContextMenu`, which makes the whole File Converter legacy context menu disappear.

The main application has the same invariant gap: `ConversionPreset.inputTypes` can be `null` after deserialization, while several methods assume it is a non-null collection.

## Goal

Make preset input-type handling fail-safe end to end so that malformed, legacy, imported, hand-edited, or empty-input presets cannot crash the application or suppress the Explorer context menu.

## Non-goals

- Do not change the existing XML schema or require a settings migration.
- Do not reject presets that intentionally have zero compatible input types.
- Do not redesign the Windows 11 modern context menu integration.
- Do not perform unrelated refactoring of preset or settings code.

## Design

### 1. Main application model invariant

`ConversionPreset.InputTypes` will be non-null for the full lifetime of a `ConversionPreset` instance.

Changes:

- Initialize the backing `inputTypes` collection to an empty `List<string>`.
- Normalize a `null` assignment in the `InputTypes` setter to an empty list.
- Normalize individual values by removing or ignoring null/empty entries before lower-casing them.
- Make `OnDeserializationComplete()`, `AddInputType()`, `RemoveInputType()`, and `CoerceInputTypes()` safe even when handling legacy or malformed data.

An empty input-type list remains valid. It means that the preset does not match any selected file until input types are configured.

### 2. Settings collection hardening

`Settings` will tolerate malformed collection data during deserialization.

Changes:

- Treat a null serialized preset array as empty.
- Skip null preset entries while rebuilding `ConversionPresets`.
- Skip null preset entries in `Clean()`, `Merge()`, and `OnDeserializationComplete()` rather than letting one malformed entry fail the whole settings object.

This keeps the existing XML schema and migration version unchanged.

### 3. Shell extension defensive boundary

The shell extension must independently treat settings XML as untrusted input because Explorer can load it without the main application having normalized it first.

Changes:

- Initialize `PresetReference.InputTypes` to an empty array and normalize null assignments where practical.
- In `CanShowMenu()` and `RefreshPresetList()`, skip null preset references and presets with null/empty `InputTypes`.
- After loading user or default settings, normalize a null preset array to an empty array.
- If both settings files fail to load, keep an empty preset array so `CanShowMenu()` returns `false` instead of throwing into Explorer.

A malformed preset will therefore affect only itself; valid presets later in the file remain usable.

### 4. Preserve current save behavior

`SettingsService.Save()` will continue to call `settings.Clean()` and serialize using the existing XML format. No new wrapper element or schema version is introduced.

A preset with zero input types may still serialize without `InputTypes` elements. On the next load, model normalization converts that absence to an empty collection, so the round trip remains safe and backward-compatible.

## Error handling

The design follows defense in depth:

1. The main application normalizes data as early as possible.
2. Settings-level iteration skips malformed entries.
3. The shell extension validates again at the Explorer boundary.
4. A bad preset never causes an exception to escape `CanShowMenu()` or prevent other valid presets from being evaluated.

No user-facing error is added for a zero-input preset because that state is currently permitted by the settings UI.

## Testing

Add a focused test project if the repository build environment supports it without introducing disproportionate framework changes. Tests should cover:

1. Deserializing a `ConversionPreset` with no `InputTypes` produces a non-null empty collection.
2. Serializing and reloading an empty-input preset preserves the non-null invariant.
3. Assigning `null` to `ConversionPreset.InputTypes` results in an empty collection.
4. Null/empty individual input-type entries do not throw during normalization.
5. Settings deserialization tolerates null preset arrays and null preset entries.
6. Shell matching skips a malformed preset and still finds a later valid `.jpg` preset.
7. Shell matching with only malformed/empty presets returns `false` rather than throwing.
8. Failure to load both user and default shell settings leaves an empty preset collection rather than `null`.

If directly testing `SharpContextMenu` state is impractical, extract only the matching decision into a small pure helper so it can be tested without Explorer or COM. Avoid broader shell-extension refactoring.

## Verification

Implementation verification will include:

- Build the solution in the environments available to the repository.
- Run the new regression tests.
- Inspect the generated diff for XML schema or serialization-version changes; there should be none.
- On Windows 11, reproduce the original case using a preset with no input types and verify that `Show more options` still contains File Converter for a supported file when another valid preset exists.
- Confirm SharpShell logging no longer reports `ArgumentNullException` from `CanShowMenu()` for that scenario.

## Compatibility

The change is intended to be backward-compatible with existing `Settings.user.xml` files, including files where `InputTypes` is absent. No settings migration or user cleanup should be required after upgrading.
