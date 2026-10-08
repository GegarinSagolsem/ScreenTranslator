using System.IO;

namespace ScreenTranslator.Tests;

[TestClass]
public sealed class AppConfigTests
{
    private readonly List<string> _files = [];

    private AppConfig LoadJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"st-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        _files.Add(path);
        return AppConfig.LoadFrom(path);
    }

    [TestCleanup]
    public void DeleteFiles() => _files.ForEach(File.Delete);

    [TestMethod]
    public void MissingFileGivesFreeGoogleDefaults()
    {
        var config = AppConfig.LoadFrom(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));

        Assert.AreEqual(TranslationService.GoogleFree, config.ActiveService);
        Assert.AreEqual("ja", config.SourceLanguage);
    }

    [TestMethod]
    public void CorruptFileFallsBackToDefaults() =>
        Assert.AreEqual(TranslationService.GoogleFree, LoadJson("{ this is not json").ActiveService);

    [TestMethod]
    public void OldConfigWithADeepLKeyKeepsUsingDeepL() =>
        Assert.AreEqual(TranslationService.DeepL, LoadJson("{\"DeepLApiKey\":\"abc:fx\"}").ActiveService);

    [TestMethod]
    public void OldConfigWithoutAKeyMovesToFreeGoogle() =>
        Assert.AreEqual(TranslationService.GoogleFree, LoadJson("{\"DeepLApiKey\":\"\"}").ActiveService);

    [TestMethod]
    public void TurnedOffGameBarHotkeyFromOldConfigsStaysOff()
    {
        var config = LoadJson("{\"GameBarHotkeyEnabled\":false}");

        Assert.IsTrue(config.GetHotkey(HotkeyCommands.GameBar).IsNone);
        Assert.IsNull(config.LegacyGameBarHotkeyEnabled);
    }

    [TestMethod]
    public void HotkeysUseDefaultsUnlessChangedOrTurnedOff()
    {
        var config = LoadJson("{\"Hotkeys\":{\"SelectRegion\":\"Ctrl+Alt+F9\",\"Peek\":\"\"}}");

        Assert.AreEqual("Ctrl+Alt+F9", config.GetHotkey(HotkeyCommands.SelectRegion).ToString());
        Assert.IsTrue(config.GetHotkey(HotkeyCommands.Peek).IsNone);
        Assert.AreEqual("Ctrl+Shift+P", config.GetHotkey(HotkeyCommands.PauseResume).ToString());
    }

    [TestMethod]
    public void OutOfRangeValuesAreClamped()
    {
        var config = LoadJson("{\"OverlayOpacity\":5,\"TextScale\":9,\"CaptureIntervalMs\":10}");

        Assert.AreEqual(1.0, config.OverlayOpacity);
        Assert.AreEqual(TranslationLayout.MaxTextScale, config.TextScale);
        Assert.AreEqual(200, config.CaptureIntervalMs);
    }
}
