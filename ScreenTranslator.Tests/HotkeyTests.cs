using System.Windows.Input;

namespace ScreenTranslator.Tests;

[TestClass]
public sealed class HotkeyTests
{
    [TestMethod]
    [DataRow("Ctrl+Shift+R", "Ctrl+Shift+R")]
    [DataRow("ctrl + alt + f9", "Ctrl+Alt+F9")]
    [DataRow("Shift+Win+3", "Win+Shift+3")]
    [DataRow("Alt+NumPad5", "Alt+NumPad5")]
    public void ParsesAndFormatsConsistently(string text, string expected)
    {
        Assert.AreEqual(expected, Hotkey.Parse(text).ToString());
        Assert.AreEqual(Hotkey.Parse(expected), Hotkey.Parse(Hotkey.Parse(text).ToString()));
    }

    [TestMethod]
    [DataRow("R", false)]
    [DataRow("Shift+R", false)]
    [DataRow("", false)]
    [DataRow("Ctrl+Shift+R", true)]
    [DataRow("Alt+X", true)]
    [DataRow("F8", true)]
    public void OnlyCombinationsThatDontBlockTypingAreSafe(string text, bool safe) =>
        Assert.AreEqual(safe, Hotkey.Parse(text).IsSafeGlobal);

    [TestMethod]
    public void DefaultHotkeysAreSafeAndUnique()
    {
        var defaults = HotkeyCommands.All.Select(c => Hotkey.Parse(c.DefaultKeys)).ToList();

        Assert.IsTrue(defaults.All(h => h.IsSafeGlobal));
        Assert.HasCount(defaults.Count, defaults.Distinct());
    }

    [TestMethod]
    public void ConvertsToWin32Codes()
    {
        var hotkey = Hotkey.Parse("Ctrl+Shift+R");

        Assert.AreEqual(0x52u, hotkey.VirtualKey);
        Assert.AreEqual(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, hotkey.Win32Modifiers);
    }

    [TestMethod]
    public void ShowsHeldModifiersWhileRecording() =>
        Assert.AreEqual("Ctrl+Shift+…", Hotkey.Pending(ModifierKeys.Control | ModifierKeys.Shift));
}
