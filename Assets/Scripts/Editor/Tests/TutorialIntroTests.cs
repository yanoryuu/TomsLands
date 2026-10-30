using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

/// <summary>
/// 画面を初めて開いたときの会話（TutorialSteps.IntroLabelsOnEnter）が、いまの仕組みと合っていること。
/// 2026-10-31 の不具合: 占い師を「上げる枠・下げる枠」に作り直したが、説明は Intro_Prophet 1 本のままで、
/// 廃止した「3 日先を占う」の頃に Intro_Prophet を見たセーブには新しい使い方が一度も流れなかった。
/// 魔王軍支援のオッズ・元本保証も、会話では説明していなかった。
/// </summary>
public class TutorialIntroTests
{
    private const string IntroCsv = "Assets/TomsScenario/Scenarios/CharacterIntro.csv";
    private const string IntroBook = "Assets/TomsScenario/Scenarios/CharacterIntro.book.asset";

    /// <summary>シートの配信の値（2026-10-31 時点のマスタ data: オッズ 2 / 1.5 / 1 / 0.45 / 0.3、元本保証 TRUE）。</summary>
    private static StreamTuningData LiveTuning() => new()
    {
        supportOddsEasy = 2f,
        supportOddsFavorable = 1.5f,
        supportOddsEven = 1f,
        supportOddsUnfavorable = 0.45f,
        supportOddsHopeless = 0.3f,
        supportPayoutFloorToStake = true,
    };

    private static string[] Unseen(TomsShopGamePhase phase, StreamTuningData d, params string[] seen) =>
        TutorialSteps.IntroLabelsOnEnter(phase, d).Where(l => !seen.Contains(l)).ToArray();

    [Test]
    public void Prophet_OldSaveThatSawIntro_StillGetsRules()
    {
        // 旧版で占い師の画面を開いたセーブ（Intro_Prophet だけ既読）
        var pending = Unseen(TomsShopGamePhase.Prophet, LiveTuning(), TutorialSteps.IntroProphet);
        CollectionAssert.AreEqual(new[] { TutorialSteps.IntroProphetRules }, pending);
    }

    [Test]
    public void Prophet_NewSave_GetsGreetingThenRules()
    {
        CollectionAssert.AreEqual(new[] { TutorialSteps.IntroProphet, TutorialSteps.IntroProphetRules },
            Unseen(TomsShopGamePhase.Prophet, LiveTuning()));
    }

    [Test]
    public void DemonLord_OldSaveThatSawIntro_GetsOddsAndFloor()
    {
        var pending = Unseen(TomsShopGamePhase.DungeonLevelUp, LiveTuning(), TutorialSteps.IntroDemonLord);
        CollectionAssert.AreEqual(new[] { TutorialSteps.IntroSupportOdds, TutorialSteps.IntroSupportFloor }, pending);
    }

    [Test]
    public void DemonLord_OddsAllOne_DoesNotTalkAboutOdds()
    {
        // 既定値（オッズが全部 1・元本保証なし）では配当は今と同じなので、オッズ・元本保証の話はしない
        CollectionAssert.AreEqual(new[] { TutorialSteps.IntroDemonLord },
            TutorialSteps.IntroLabelsOnEnter(TomsShopGamePhase.DungeonLevelUp, new StreamTuningData()));
    }

    [Test]
    public void AllIntroLabels_ExistInCsvAndImportedBook()
    {
        string csv = File.ReadAllText(IntroCsv);
        string book = File.ReadAllText(IntroBook);
        foreach (var phase in new[] { TomsShopGamePhase.Prophet, TomsShopGamePhase.DungeonLevelUp })
        foreach (var label in TutorialSteps.IntroLabelsOnEnter(phase, LiveTuning()))
        {
            StringAssert.Contains($"\n*{label},", csv, $"{label} が CSV に無い");
            // 宴のブックには「- '*ラベル'」の行で入る（CSV を直したら宴の再インポートが要る）
            StringAssert.Contains($"- '*{label}'", book, $"{label} が {IntroBook} に無い（宴の再インポート漏れ）");
        }
    }

    [Test]
    public void ProphetRules_NumbersMatchTuning()
    {
        string text = LabelText(TutorialSteps.IntroProphetRules);
        StringAssert.Contains($"最大{ProphetTuning.MaxPower}つ", text);
        StringAssert.Contains($"配信を1回終えるたびに{ProphetTuning.PowerPerStream}つ戻る", text);
        StringAssert.Contains($"{ProphetTuning.EffectTurns}日のあいだ", text);
        StringAssert.Contains("上げる枠", text);
        StringAssert.Contains("下げる枠", text);
    }

    [Test]
    public void ProphetTalk_DoesNotDescribeRemovedFortune()
    {
        // 廃止した「3 日先を占う」・3 件の一覧（荒れ相場・高値・おすすめ）の説明が残っていない
        string text = LabelText(TutorialSteps.IntroProphet) + LabelText(TutorialSteps.IntroProphetRules);
        foreach (var removed in new[] { "先を占", "流行をボクのタロットで占", "トレンド武器", "おすすめ" })
            StringAssert.DoesNotContain(removed, text);
    }

    [Test]
    public void ProphetDialogues_UseDaysNotTurns()
    {
        // 時間の呼び名は「日」（Docs/Wording_Glossary.md）。会話と画面のセリフで食い違わないように
        string csv = File.ReadAllText("Assets/Resources_moved/ProphetDialogues.csv");
        StringAssert.DoesNotContain("ターン", csv);
    }

    [Test]
    public void FundsAdvance_SaysAmountAndRepayment()
    {
        // 立て替えは次の納付金に上乗せして返す（TomsModel.GrantFundsAdvance → DebtCalculator）。会話が「返済不要」に読めないこと
        string text = LabelText(TutorialSteps.FundsAdvance, TutorialCsv);
        StringAssert.Contains($"{TutorialSteps.FundsAdvanceAmount:N0}G立て替え", text);
        StringAssert.Contains("次の納付金に上乗せ", text);
    }

    [Test]
    public void FundsShortage_DoesNotPromiseEverythingSold()
    {
        // 1 日の客の数には上限がある（ItemModel.GetDailyCustomers）ので、「ぜんぶ売れた」とは言わない
        StringAssert.DoesNotContain("ぜんぶ注文", LabelText(TutorialSteps.FundsShortage, TutorialCsv));
        StringAssert.Contains("お客さんの数には限り", LabelText(TutorialSteps.Display, TutorialCsv));
    }

    private const string TutorialCsv = "Assets/TomsScenario/Scenarios/Tutorial.csv";

    /// <summary>CSV のラベルの会話のセリフ（9 列目）をつなげて返す。</summary>
    private static string LabelText(string label, string csvPath = IntroCsv)
    {
        var lines = File.ReadAllLines(csvPath);
        int start = System.Array.FindIndex(lines, l => l.StartsWith($"*{label},"));
        Assert.GreaterOrEqual(start, 0, $"{label} が CSV に無い");
        var sb = new System.Text.StringBuilder();
        for (int i = start + 1; i < lines.Length && !lines[i].StartsWith("EndScenario"); i++)
        {
            var m = Regex.Match(lines[i], "\"([^\"]*)\"");
            if (m.Success) sb.Append(m.Groups[1].Value).Append('\n');
        }
        return sb.ToString();
    }
}
