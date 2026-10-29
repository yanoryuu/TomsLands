using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// 人格プレイのスターターレリック: 質問の組み立て（候補がそのまま選択肢になる・「選ばない」は無い）と、
/// 人格ごとの好み（ずんだもんは派手なもの、めたんは手堅いもの）、めたんの選び方がいつも 1 番目（子豚の貯金箱）にならないこと。
/// </summary>
public class StarterRelicPersonaTests
{
    // 出店準備の Common の並び（1 番目が子豚の貯金箱）
    private static readonly List<AutoPlayStarterRelicChoice> Commons = new()
    {
        new AutoPlayStarterRelicChoice(0, "relic_piggy", "子豚の貯金箱", "振ると毎朝3,000Gが出てくる。", RelicRarity.Common),
        new AutoPlayStarterRelicChoice(1, "relic_fairy_coin", "妖精のコイン", "イベントで得る金額+50%。", RelicRarity.Common),
        new AutoPlayStarterRelicChoice(2, "relic_gossip_notes", "話題のネタ帳", "バズ発生率+5%。", RelicRarity.Common),
        new AutoPlayStarterRelicChoice(3, "relic_showcase", "折りたたみ陳列棚", "同時に並べられる品の種類+1。", RelicRarity.Common),
        new AutoPlayStarterRelicChoice(4, "relic_signboard", "手描きの看板", "営業売上+10%。", RelicRarity.Common),
        new AutoPlayStarterRelicChoice(5, "relic_wholesaler_pass", "問屋の顔パス", "仕入れ価格-5%。", RelicRarity.Common),
    };

    [Test]
    public void Request_OffersEveryCandidate_WithoutNone()
    {
        var req = PersonaRequests.BuildStarterRelicRequest(PersonaProfile.Zundamon, "test", null, null, Commons);
        Assert.IsNotNull(req);
        Assert.IsTrue(req.Questions.TryGetValue("starter_relic", out var q));
        Assert.AreEqual("choice", q.Type);
        var criteria = q.Criteria as Dictionary<string, string>;
        Assert.IsNotNull(criteria);
        CollectionAssert.AreEquivalent(Commons.Select(c => c.Id), criteria.Keys);
        Assert.IsFalse(criteria.ContainsKey("none"), "レリックはただでもらえるので「選ばない」は選択肢に入れない");

        Assert.IsNull(PersonaRequests.BuildStarterRelicRequest(PersonaProfile.Zundamon, "test", null, null, Commons.Take(1).ToList()),
            "候補が 1 つなら聞かない");
    }

    [Test]
    public void Taste_ZundamonLikesFlashy_MetanLikesSteady()
    {
        var z = PersonaStrategy.StarterRelicTaste(PersonaProfile.Zundamon, Commons);
        var m = PersonaStrategy.StarterRelicTaste(PersonaProfile.Metan, Commons);
        Assert.Greater(z["relic_gossip_notes"], z["relic_piggy"], "ずんだもんは派手なもの（バズ）に寄る");
        Assert.Greater(m["relic_piggy"], m["relic_gossip_notes"], "めたんは手堅いもの（毎朝のお金）に寄る");
        Assert.Greater(m["relic_wholesaler_pass"], m["relic_fairy_coin"]);
    }

    [Test]
    public void MetanCareful_ReturnsValidKey_AndIsNotAlwaysFirst()
    {
        var ids = Commons.Select(c => c.Id).ToList();
        var kev = ids.ToDictionary(id => id, _ => 1f / ids.Count);
        var mult = PersonaStrategy.StarterRelicTaste(PersonaProfile.Metan, Commons);
        var counts = new Dictionary<string, int>();
        const int trials = 200;
        for (int seed = 0; seed < trials; seed++)
        {
            var res = PersonaChooser.Careful(new System.Random(seed), PersonaProfile.Metan, kev, kev, mult, false, PersonaChooser.EntropyFloorExplore);
            Assert.Contains(res.Key, ids);
            counts[res.Key] = (counts.TryGetValue(res.Key, out var n) ? n : 0) + 1;
        }
        Assert.GreaterOrEqual(counts.Count, 2, "1 つに固まらない");
        Assert.Less(counts.TryGetValue("relic_piggy", out var piggy) ? piggy : 0, trials, "いつも 1 番目（子豚の貯金箱）にはならない");
    }
}
