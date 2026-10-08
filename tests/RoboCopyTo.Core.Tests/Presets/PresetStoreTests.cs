using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;
using RoboCopyTo.Core.Presets;
using RoboCopyTo.Core.Tests.Integration;

namespace RoboCopyTo.Core.Tests.Presets;

public sealed class PresetStoreTests : IDisposable
{
    private readonly TempDir _t = new();
    private string PresetsPath => _t["presets.json"];

    public void Dispose() => _t.Dispose();

    [Fact]
    public void Built_ins_match_spec()
    {
        var store = new PresetStore(PresetsPath);
        var ctx = new BuildContext(@"C:\l.log");
        string Args(string name) => CommandBuilder.Build([new SelectedItem(@"C:\A", ItemKind.Folder)], @"D:\B", store.Find(name)!.Options, ctx)[0].Arguments;

        Assert.Equal([Preset.QuickCopy, Preset.MirrorBackup, Preset.LargeFilesOverNetwork], store.All.Select(p => p.Name));
        Assert.All(store.All, p => Assert.True(p.IsBuiltIn));
        Assert.Equal(new RobocopyOptions(), store.Find(Preset.QuickCopy)!.Options);
        Assert.Contains(" /E /COPY:DAT /DCOPY:DAT /MT:8 /R:3 /W:5 ", Args(Preset.QuickCopy));
        Assert.Contains(" /MIR /COPY:DAT /DCOPY:DAT /MT:8 ", Args(Preset.MirrorBackup));
        Assert.Contains(" /E /COPY:DAT /DCOPY:DAT /Z /MT:4 /R:10 /W:15 /TBD ", Args(Preset.LargeFilesOverNetwork));
    }

    [Fact]
    public void Custom_presets_survive_reload()
    {
        var options = new RobocopyOptions { Threads = 32, ExcludeFiles = "*.tmp", ExistingFiles = ExistingFileMode.OnlyNewer, CopyOwner = true };
        new PresetStore(PresetsPath).Save(new Preset("Mine", options));

        var reloaded = new PresetStore(PresetsPath);
        var p = reloaded.Find("mine");
        Assert.NotNull(p);
        Assert.False(p.IsBuiltIn);
        Assert.Equal(options, p.Options);
    }

    [Fact]
    public void Save_replaces_preset_with_same_name()
    {
        var store = new PresetStore(PresetsPath);
        store.Save(new Preset("Mine", new RobocopyOptions { Threads = 2 }));
        store.Save(new Preset("MINE", new RobocopyOptions { Threads = 3 }));
        var custom = Assert.Single(new PresetStore(PresetsPath).All, p => !p.IsBuiltIn);
        Assert.Equal(3, custom.Options.Threads);
    }

    [Fact]
    public void Built_ins_cannot_be_saved_over_or_deleted()
    {
        var store = new PresetStore(PresetsPath);
        Assert.Throws<InvalidOperationException>(() => store.Save(new Preset("quick copy", new RobocopyOptions { Threads = 1 })));
        Assert.Throws<InvalidOperationException>(() => store.Delete(Preset.MirrorBackup));
        Assert.Equal(new RobocopyOptions(), store.Find(Preset.QuickCopy)!.Options);
        Assert.False(File.Exists(PresetsPath));
    }

    [Fact]
    public void Built_in_can_be_copied_with_save_as()
    {
        var store = new PresetStore(PresetsPath);
        var copy = store.Save(store.Find(Preset.MirrorBackup)! with { Name = "My mirror" });
        Assert.False(copy.IsBuiltIn);
        Assert.True(new PresetStore(PresetsPath).Find("My mirror")!.Options.Mirror);
    }

    [Fact]
    public void Delete_removes_custom_preset()
    {
        var store = new PresetStore(PresetsPath);
        store.Save(new Preset("Gone", new RobocopyOptions()));
        store.Delete("gone");
        Assert.Null(new PresetStore(PresetsPath).Find("Gone"));
    }

    [Fact]
    public void Built_in_names_in_file_are_ignored()
    {
        File.WriteAllText(PresetsPath, """{ "presets": [ { "name": "Quick copy", "options": { "threads": 1 } } ] }""");
        Assert.Equal(8, new PresetStore(PresetsPath).Find(Preset.QuickCopy)!.Options.Threads);
    }

    [Fact]
    public void Corrupt_file_is_kept_as_a_backup_and_never_overwritten()
    {
        File.WriteAllText(PresetsPath, "{ not json, my precious presets");
        var store = new PresetStore(PresetsPath);

        Assert.Equal(3, store.All.Count);
        Assert.NotNull(store.LoadWarning);
        var backup = Assert.Single(Directory.GetFiles(_t.Root, "presets.json.bad-*"));
        Assert.Equal("{ not json, my precious presets", File.ReadAllText(backup));

        store.Save(new Preset("New", new RobocopyOptions()));
        Assert.Equal("{ not json, my precious presets", File.ReadAllText(backup));
    }

    [Fact]
    public void Unknown_enum_value_keeps_the_file_as_a_backup()
    {
        File.WriteAllText(PresetsPath, """{ "presets": [ { "name": "Mine", "options": { "existingFiles": "Sometimes" } } ] }""");
        var store = new PresetStore(PresetsPath);
        Assert.NotNull(store.LoadWarning);
        Assert.Single(Directory.GetFiles(_t.Root, "presets.json.bad-*"));
    }

    [Theory]
    [InlineData("""{ "presets": null }""")]
    [InlineData("""{ "presets": [ null ] }""")]
    [InlineData("""{ }""")]
    [InlineData("""{ "presets": [ { "name": null, "options": {} } ] }""")]
    [InlineData("""{ "presets": [ { "name": "x", "options": null } ] }""")]
    public void Null_values_do_not_crash(string json)
    {
        File.WriteAllText(PresetsPath, json);
        var store = new PresetStore(PresetsPath);
        Assert.Equal(3, store.All.Count);
        Assert.Null(store.LoadWarning);
    }

    [Fact]
    public void Null_strings_in_options_become_empty()
    {
        File.WriteAllText(PresetsPath, """{ "presets": [ { "name": "Mine", "options": { "extraArguments": null, "excludeFiles": null, "threads": 500 } } ] }""");
        var o = new PresetStore(PresetsPath).Find("Mine")!.Options;
        Assert.Equal("", o.ExtraArguments);
        Assert.Equal("", o.ExcludeFiles);
        Assert.Equal(128, o.Threads);
    }

    [Fact]
    public void Unreadable_file_gives_a_warning_not_a_crash()
    {
        File.WriteAllText(PresetsPath, """{ "presets": [] }""");
        using var locked = new FileStream(PresetsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var store = new PresetStore(PresetsPath);
        Assert.NotNull(store.LoadWarning);
        Assert.Equal(3, store.All.Count);
    }

    [Fact]
    public void Failed_save_leaves_presets_unchanged()
    {
        var store = new PresetStore(PresetsPath);
        store.Save(new Preset("Kept", new RobocopyOptions()));
        using (new FileStream(PresetsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<Exception>(() => store.Save(new Preset("Lost", new RobocopyOptions())));
            Assert.ThrowsAny<Exception>(() => store.Delete("Kept"));
        }
        Assert.Equal(["Kept"], store.All.Where(p => !p.IsBuiltIn).Select(p => p.Name));
        Assert.Empty(Directory.GetFiles(_t.Root, "*.tmp"));
    }

    [Fact]
    public void Corrupt_settings_are_kept_as_a_backup()
    {
        var path = _t["settings.json"];
        File.WriteAllText(path, "not json");
        var s = AppSettings.Load(path);
        Assert.NotNull(s.LoadWarning);
        Assert.Empty(s.RecentDestinations);
        Assert.Single(Directory.GetFiles(_t.Root, "settings.json.bad-*"));
    }

    [Fact]
    public void Null_recent_destinations_load_as_empty()
    {
        var path = _t["settings.json"];
        File.WriteAllText(path, """{ "recentDestinations": null }""");
        Assert.Empty(AppSettings.Load(path).RecentDestinations);
    }

    [Fact]
    public void File_is_indented_readable_json_without_paths_or_computed_values()
    {
        new PresetStore(PresetsPath).Save(new Preset("Mine", new RobocopyOptions { ExistingFiles = ExistingFileMode.SkipExisting }));
        var json = File.ReadAllText(PresetsPath);
        Assert.Contains("\n", json);
        Assert.Contains("  \"presets\": [", json);
        Assert.Contains("\"existingFiles\": \"SkipExisting\"", json);
        Assert.DoesNotContain("effective", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isBuiltIn", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preset_model_has_no_source_or_destination_fields()
    {
        // Presets serialize exactly RobocopyOptions; guard against a path-like property creeping in.
        var names = typeof(RobocopyOptions).GetProperties().Select(p => p.Name)
            .Concat(typeof(Preset).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain(names, n => n.Contains("Source", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Destination", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Settings_round_trip_and_recent_destinations_mru_capped_at_ten()
    {
        var path = _t["settings.json"];
        var s = new AppSettings();
        for (var i = 0; i < 12; i++)
            s.AddRecentDestination($@"D:\Dest{i}");
        s.AddRecentDestination(@"d:\dest5\");
        s.DefaultPreset = "Mine";
        s.LastUsedPreset = Preset.MirrorBackup;
        s.Save(path);

        var loaded = AppSettings.Load(path);
        Assert.Equal(10, loaded.RecentDestinations.Count);
        Assert.Equal(@"d:\dest5\", loaded.RecentDestinations[0]);
        Assert.Equal(@"D:\Dest11", loaded.RecentDestinations[1]);
        Assert.Single(loaded.RecentDestinations, d => d.TrimEnd('\\').Equals(@"D:\Dest5", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Mine", loaded.DefaultPreset);
        Assert.Equal(Preset.MirrorBackup, loaded.LastUsedPreset);
    }

    [Fact]
    public void Initial_preset_prefers_default_then_last_used_then_quick_copy()
    {
        var store = new PresetStore(PresetsPath);
        store.Save(new Preset("Mine", new RobocopyOptions()));
        Assert.Equal("Mine", new AppSettings { DefaultPreset = "mine", LastUsedPreset = Preset.MirrorBackup }.InitialPresetName(store));
        Assert.Equal(Preset.MirrorBackup, new AppSettings { DefaultPreset = "deleted one", LastUsedPreset = Preset.MirrorBackup }.InitialPresetName(store));
        Assert.Equal(Preset.QuickCopy, new AppSettings().InitialPresetName(store));
    }
}
