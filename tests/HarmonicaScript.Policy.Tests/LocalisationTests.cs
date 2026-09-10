using HarmonicaScript.App.I18n;
using HarmonicaScript.Core.Conversion;

namespace HarmonicaScript.Policy.Tests;

/// <summary>
/// Prevents the classic half-translated release: a UI where the language toggle works but a third
/// of the strings silently stay English because someone added a key and forgot the other table.
/// </summary>
public sealed class LocalisationTests
{
    [Fact]
    public void EveryEnglishKeyHasAChineseTranslation()
    {
        var missing = Loc.EnglishTable.Keys.Where(k => !Loc.ChineseTable.ContainsKey(k)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryChineseKeyExistsInTheNeutralTable()
    {
        // The reverse direction catches an orphaned translation, which is how a key rename
        // quietly leaves dead strings behind.
        var orphans = Loc.ChineseTable.Keys.Where(k => !Loc.EnglishTable.ContainsKey(k)).ToList();
        Assert.Empty(orphans);
    }

    [Fact]
    public void NoStringIsEmpty()
    {
        Assert.All(Loc.EnglishTable, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
        Assert.All(Loc.ChineseTable, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
    }

    [Fact]
    public void ChineseStringsAreActuallyChinese()
    {
        // A copy-pasted English value in the zh table would pass a key-parity check and still be
        // a half-translated release. Exempt the handful of keys that are proper nouns or toggles.
        string[] exempt = ["App_Title", "Common_Language", "Editor_Sharp"];

        var suspicious = Loc.ChineseTable
            .Where(kv => !exempt.Contains(kv.Key))
            .Where(kv => !kv.Value.Any(c => c >= 0x4E00 && c <= 0x9FFF))
            .Select(kv => kv.Key)
            .ToList();

        Assert.Empty(suspicious);
    }

    [Fact]
    public void SwitchingCultureChangesEveryVisibleString()
    {
        var loc = Loc.Instance;
        loc.SetCulture("en");
        var english = loc["Nav_Report"];

        loc.SetCulture("zh-Hans");
        var chinese = loc["Nav_Report"];

        Assert.NotEqual(english, chinese);
        Assert.True(loc.IsChinese);

        loc.SetCulture("en");
        Assert.Equal(english, loc["Nav_Report"]);
    }

    [Fact]
    public void SimplifiedChineseRegionsAllResolveToTheSameTable()
    {
        // zh-CN, zh-SG and zh-MY fall back to zh-Hans through .NET's own chain, which is exactly
        // why the table is keyed on the script rather than on a region.
        var loc = Loc.Instance;
        foreach (var culture in new[] { "zh-CN", "zh-Hans", "zh-SG", "zh" })
        {
            loc.SetCulture(culture);
            Assert.True(loc.IsChinese, culture);
        }

        loc.SetCulture("en-US");
        Assert.False(loc.IsChinese);
    }

    [Fact]
    public void AMissingKeyIsVisibleRatherThanBlank()
    {
        Assert.Equal("[Nope_NotAKey]", Loc.Instance["Nope_NotAKey"]);
    }

    [Fact]
    public void TheDisclaimerNamesBothProhibitedCategoriesHonestly()
    {
        // The verified fact this rests on: Tencent's official prohibited-software list names BOTH
        // automation scripts AND mouse/hardware macros. A disclaimer that implied macro export
        // were a safe harbour would be a false claim to users.
        var store = new Profiles.ProfileStore();
        var game = store.LoadGame("df.v1");
        var notice = game.Value.ProhibitedSoftwareNotice!;

        Assert.Contains("Autohotkey", notice.ZhHans!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("鼠标宏", notice.ZhHans!, StringComparison.Ordinal);
        Assert.Contains("两种模式都在禁止范围内", notice.ZhHans!, StringComparison.Ordinal);
        Assert.Contains("不是政策上的安全港", notice.ZhHans!, StringComparison.Ordinal);

        // The precise distinction the whole risk posture rests on: macro export is 更安静
        // (quieter, in detection terms), never 更安全 (safer, in policy terms).
        Assert.Contains("更安静", notice.ZhHans!, StringComparison.Ordinal);
        Assert.DoesNotContain("更安全", notice.ZhHans!, StringComparison.Ordinal);
        Assert.DoesNotContain("防封", notice.ZhHans!, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingInTheProductClaimsItIsUndetectable()
    {
        // A permanent commitment, enforced rather than remembered. These words must never appear
        // in shipped strings, in any language.
        string[] forbidden = ["防封", "过检测", "undetectable", "100% safe", "更安全", "不会被发现"];

        foreach (var table in new[] { Loc.EnglishTable, Loc.ChineseTable })
        {
            foreach (var (key, value) in table)
            {
                foreach (var word in forbidden)
                {
                    Assert.False(
                        value.Contains(word, StringComparison.OrdinalIgnoreCase),
                        $"string '{key}' contains the forbidden claim '{word}'");
                }
            }
        }
    }

    [Fact]
    public void EveryDiagnosticCodeIsAccountedFor()
    {
        // The core returns codes, never prose. If a new code has no presentation anywhere, it
        // reaches a user as a bare enum name.
        var codes = Enum.GetValues<DiagnosticCode>().Where(c => c != DiagnosticCode.None).ToList();
        Assert.NotEmpty(codes);
        Assert.All(codes, c => Assert.False(string.IsNullOrWhiteSpace(c.ToString())));
    }
}
