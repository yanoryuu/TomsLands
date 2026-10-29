#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// =====================================================================
// 相談プレイ（ずんだもんが遊び、四国めたんに相談しながら進める）。
// 判断ごとに: ずんだもんの案（kev 4B・彼の選び方）→ めたんの助言（kev 9B・彼女の選び方。賛成 / 反対 / 条件つき、画面の数字の理由）
// → ずんだもんが聞くか聞かないか決める（めたんの自信・自分の自信・性格・これまでの「聞いて得したか」）。
// 結果が出たら、聞かずに外したら「だから言ったでしょ」、聞かずに当てたらマウント、聞いて当てたら渋々のお礼。
// 会話は Recordings/consult_log_<ラン ID>.jsonl に台本の形で残す（tools/persona_play/consult_to_script.py で jikkyo.py の台本にできる）。
// 人物像は tools/jikkyo_video/CHARACTERS.md。ミーム音は tools/jikkyo_video/assets/sounds_meme/index.json で
// use・verified が true で、話し手・場面が合うと確かめたものだけ（ConsultSounds.Scenes）。
// =====================================================================

/// <summary>相談の理由に使う画面の数字（分からないものは -1）。</summary>
public sealed class ConsultFacts
{
    public int Cash = -1;
    public int DebtAmount;
    public int DebtDaysLeft = -1;
    public int StreamDays = -1;
    public bool WeaknessKnown;
    public string Weakness;
    public int IntelCost = -1;
    public int SupportCost = -1;
    public int DefeatReward = -1;
    public int RewardAfterSupport = -1;
    public int HeroHpPct = -1;
    public int Monsters = -1;
    public bool Boss;
    public int SpendBudget = -1;
    public bool SupportPaid;
    public string Side;
    public int Funds = -1;
    public int Reserve = -1;
    public int Needed;              // 村: めたんが元が取れると見た施設に要る額（残す額の内側）
    public string NeededNames;      // 村: その施設の名前（「酒場と職人街」）
    public string FacilityName;
    public int Cost = -1;
    public int LastProfit;
    public string ItemName;         // 店への投資・勇者の装備: 選んだもの（日本語）
    public int PaybackDays = -1;    // 設備: 元が取れる日数（お金の設備だけ）
}

/// <summary>片方の答え。</summary>
public sealed class ConsultPick
{
    public string Key;
    public string Label;          // 日本語（「12万G」「全力」「勇者」「買う」など。段階の質問も割合ではなく判断時点の額で言う）
    public float Conf;            // kev の確信度（EffectiveConfidence）
    public Dictionary<string, float> Probs;
    public string Why;            // 選び方の理由（margin / close / explore / sample …）
    public long? LatencyMs;
}

public sealed class ConsultRequest
{
    public string Question;
    public string Phase;          // day / stream / village / in_stream
    public int Day;
    public bool Levels;
    public int MaxLevel;
    public ConsultPick Z;
    public ConsultPick M;
    public string ZLine;
    public string ZMeme;
    public PersonaLines.Band ZBand;
    public ConsultFacts Facts;
    /// <summary>決まりきった判断（新聞・村の施設ごと）。同じ意見なら会話しない。</summary>
    public bool SilentAgree;
    /// <summary>意見が割れたときに声に出す確率（新聞のような小さな判断は低く。出さなければずんだもんの案のまま）。</summary>
    public float VoiceChance = 1f;
    /// <summary>めたんが崩れている（マウントされて慎重策を捨てた。1 ランに 1 回）。</summary>
    public bool MCollapse;
}

public sealed class ConsultOutcome
{
    public string Final;
    public bool Followed;
    public string Relation;       // agree / condition / disagree / solo
    public int Exchange;
}

public sealed class PersonaConsult
{
    private readonly System.Random _rng;
    private readonly PersonaLines.MemeRun _memes;
    private readonly ConsultLog _log;
    private readonly ConsultMemory _mem;
    private readonly ConsultSounds _snd;
    private readonly PersonaMemory _zMem;
    private readonly PersonaMemory _mMem;
    /// <summary>2 人の気分（遊ぶ側 = ずんだもん、助言する側 = めたん）。後日談で動き、表情・聞く確率・後日談の選び方に効く。</summary>
    private readonly PersonaMood _zMood;
    private readonly PersonaMood _mMood;
    private int _exchange;
    private readonly Dictionary<string, Bound> _bound = new();
    private readonly List<(Bound b, int reward)> _resolved = new();
    private readonly Dictionary<string, (string key, string relation)> _lastByQuestion = new();
    private readonly Dictionary<string, List<string>> _recent = new();
    private int _lastFollowUpDay = int.MinValue;
    private int _collapseBeatDay = int.MinValue;
    /// <summary>このランの出来事（後日談の told / mount）。掛け合いの呼び戻し（「この前も〜で外したでしょ」）に使う。</summary>
    private readonly List<(string kind, string group, string label, int day)> _events = new();
    /// <summary>めたんのまっすぐな褒め（1 ランに 1 回まで。CHARACTERS.md: そこが山場）。</summary>
    private bool _praiseUsed;

    private sealed class Bound
    {
        public int Exchange;
        public string Question;
        public string Relation;
        public bool Followed;
        public string ZKey, MKey, Final, ZLabel, MLabel;
        public int Day;
        public string Horizon;
        public bool MCollapse;
    }

    public string LogPath => _log.FilePath;

    public PersonaConsult(System.Random rng, PersonaLines.MemeRun memes, PersonaMemory zMem, PersonaMemory mMem,
        PersonaMood zMood = null, PersonaMood mMood = null)
    {
        _rng = rng ?? new System.Random();
        _memes = memes;
        _zMem = zMem;
        _mMem = mMem;
        _zMood = zMood ?? new PersonaMood(PersonaTraits.Zundamon);
        _mMood = mMood ?? new PersonaMood(PersonaTraits.Metan);
        _log = new ConsultLog();
        _mem = ConsultMemory.Load();
        _snd = ConsultSounds.Load();
    }

    public void ResetRun()
    {
        _snd.ResetRun();
        _events.Clear();
        _praiseUsed = false;
    }

    /// <summary>同じ種類の質問で、このランの前の日に起きた出来事（新しい順の最初）。</summary>
    private (string kind, string group, string label, int day)? PastEvent(string kind, string question, int day)
    {
        string g = Group(question);
        for (int i = _events.Count - 1; i >= 0; i--)
            if (_events[i].kind == kind && _events[i].group == g && _events[i].day < day) return _events[i];
        return null;
    }

    public string Summary =>
        $"相談ログ {_log.FilePath} / ミーム音 {_snd.Usable} 件（確かめた場面のもの）/ ずんだもんの信頼 {_mem.TrustSummary()}";

    // =====================================================
    // 判断 1 つの相談
    // =====================================================

    public ConsultOutcome Exchange(ConsultRequest r)
    {
        var o = new ConsultOutcome { Final = r.Z.Key, Followed = false, Relation = "solo" };
        if (r.M == null || string.IsNullOrEmpty(r.M.Key)) return o;
        bool same = r.Z.Key == r.M.Key;
        int diff = same ? 0 : r.Levels ? Math.Abs(Level(r.Z.Key) - Level(r.M.Key)) : 1;
        o.Relation = same ? "agree" : (r.Levels && diff == 1) || (!r.Levels && r.M.Conf < 0.35f) ? "condition" : "disagree";
        o.Exchange = ++_exchange;
        var stat = _mem.Advice(r.Question);
        stat.Asked++;

        var decision = new Dictionary<string, object>
        {
            ["question"] = r.Question,
            ["z"] = PickJson(r.Z),
            ["m"] = PickJson(r.M),
            ["relation"] = o.Relation,
            ["moodZ"] = _zMood.Snapshot(),
            ["moodM"] = _mMood.Snapshot(),
        };

        bool quietSplit = !same && r.VoiceChance < 1f && _rng.NextDouble() >= r.VoiceChance;
        if (quietSplit)
        {
            // 小さな判断で意見が割れても、毎回は口に出さない（ずんだもんの案のまま、聞いたことにもしない）
            o.Relation = "unvoiced";
            decision["relation"] = o.Relation;
            decision["final"] = r.Z.Key;
            decision["followed"] = null;
            _log.Write(Entry(r, o.Exchange, 0, "silent", null, null, null, decision));
            _mem.Save();
            return o;
        }

        if (same && r.SilentAgree)
        {
            stat.Agreed++;
            decision["final"] = r.Z.Key;
            decision["followed"] = null;
            _log.Write(Entry(r, o.Exchange, 0, "silent", null, null, null, decision));
            _mem.Save();
            return o;
        }

        int seq = 0;
        string zMemeReaction = r.ZMeme != null ? _memes?.Lexicon?.Memes.FirstOrDefault(m => m.Id == r.ZMeme)?.MetanReaction : null;
        var propose = Entry(r, o.Exchange, seq++, "propose", "z", r.ZLine, PersonaLines.Expression("zundamon", r.ZBand, _zMood.Label, r.Z.Conf,
            _zMood.Conf < _zMood.ConfBase - 0.05f));
        if (r.ZMeme != null) propose["meme"] = r.ZMeme;
        _log.Write(propose);

        if (same)
        {
            stat.Agreed++;
            bool confident = r.Z.Conf >= 0.5f && r.M.Conf >= 0.5f;
            bool repeated = _lastByQuestion.TryGetValue(r.Question, out var last) && last.key == r.Z.Key && last.relation == "agree";
            decision["final"] = r.Z.Key;
            decision["followed"] = null;
            if (repeated && _rng.NextDouble() < 0.5)
            {
                // 同じ判断が続くときは、めたんは黙ってうなずくだけ（会話を繰り返さない）
                _log.Write(Entry(r, o.Exchange, seq, "nod", null, null, null, decision));
            }
            else
            {
                bool ack = confident || _rng.NextDouble() < 0.5;
                string reply = ack ? Line("m|ack", r, null) : Line("m|agree", r, Reason(r, r.M.Key, true));
                // 同じ行を言い過ぎるときは（1 ラン 3 回・20 行あける）、黙ってうなずくだけ
                _log.Write(string.IsNullOrEmpty(reply)
                    ? Entry(r, o.Exchange, seq, "nod", null, null, null, decision)
                    : Entry(r, o.Exchange, seq, ack ? "ack" : "agree", "m", reply, "normal", decision));
            }
            _lastByQuestion[r.Question] = (r.Z.Key, "agree");
            _mem.Save();
            return o;
        }

        // ---- 反対 / 条件つき ----
        if (!string.IsNullOrEmpty(zMemeReaction) && PersonaLines.IsClean(zMemeReaction))
        {
            var react = Entry(r, o.Exchange, seq++, "meme_reaction", "m", zMemeReaction, "tsun");
            react["meme"] = r.ZMeme;
            _log.Write(react);
        }
        bool big = IsBigBet(r);
        string adviceKey = r.Question == "intervene_now"
            ? (r.M.Key == "none" ? "m|instream_wait" : "m|instream_other")
            : o.Relation == "condition" ? "m|condition" : "m|disagree";
        string past = null;
        if (adviceKey == "m|disagree")
        {
            // 呼び戻し（P6）: 同じ種類の質問で前に「聞かずに外した」なら、それを持ち出す。金欠の事実（3 か月分のお小遣い）は大きな賭けに
            var told = PastEvent("told", r.Question, r.Day);
            if (told != null && told.Value.label != r.M.Label && _rng.NextDouble() < 0.45)
            {
                adviceKey = "m|disagree_callback";
                past = told.Value.label;
            }
            else if (big && r.Question is "budget" or "support" or "intensity" && _rng.NextDouble() < 0.35) adviceKey = "m|disagree_money";
        }
        var advise = Entry(r, o.Exchange, seq++, o.Relation, "m", Line(adviceKey, r, Reason(r, r.M.Key, false), past),
            o.Relation == "condition" ? "normal" : "tsun");
        if (past != null) advise["beat"] = "callback";
        if (o.Relation == "disagree") _snd.Attach(advise, big ? "m_disagree_big" : "m_disagree", "m", _rng);
        if (r.MCollapse && _collapseBeatDay != r.Day)
        {
            // めたんが崩れた（マウントされて慎重策を捨てた）最初の助言。表情は hero（「次は絶対当てる」）
            _collapseBeatDay = r.Day;
            advise["beat"] = "collapse";
            advise["e"] = "hero";
        }
        _log.Write(advise);

        float p = FollowProbability(r, o.Relation);
        o.Followed = _rng.NextDouble() < p;
        o.Final = o.Followed ? r.M.Key : r.Z.Key;
        if (o.Followed) stat.Followed++;
        else stat.Ignored++;
        decision["pFollow"] = Math.Round(p, 3);
        decision["followed"] = o.Followed;
        decision["final"] = o.Final;

        Dictionary<string, object> decide;
        if (o.Followed)
            decide = Entry(r, o.Exchange, seq++, "follow", "z", Line("z|follow", r, null), _zMood.Label == "sad" ? "namida" : "hiso", decision);
        else
        {
            string scene = big ? IgnoreScene(r) : null;
            string key = scene switch
            {
                "z_ignore_daijoubu" => "z|ignore_daijoubu",
                "z_ignore_bet" => "z|ignore_bet",
                "z_ignore_power" => "z|ignore_power",
                _ => "z|ignore",
            };
            string zPast = null;
            if (scene == null)
            {
                // 呼び戻し（P6）: 前に当てた（マウント）なら「前も当てた」、外したなら「前のは忘れた」
                var mount = PastEvent("mount", r.Question, r.Day);
                var lost = PastEvent("told", r.Question, r.Day);
                if (mount != null && _rng.NextDouble() < 0.45)
                {
                    key = "z|ignore_mount_callback";
                    zPast = mount.Value.label;
                }
                else if (lost != null && _rng.NextDouble() < 0.35)
                {
                    key = "z|ignore_told_callback";
                    zPast = lost.Value.label;
                }
            }
            string zLine = Line(key, r, null, zPast);
            if (string.IsNullOrEmpty(zLine))
            {
                // 同じ押し切りの行を言い過ぎた（1 ラン 3 回・20 行あける）: ふつうの押し切りにして、音も付けない
                key = "z|ignore";
                scene = null;
                zPast = null;
                zLine = Line(key, r, null);
            }
            // 押し切るときの表情: ドヤ → tsun、大きな賭け → ama、それ以外（しょんぼりからの取り返しも）→ hero
            string ignoreFace = _zMood.Label == "smug" ? "tsun" : big ? "ama" : "hero";
            decide = Entry(r, o.Exchange, seq++, "ignore", "z", zLine, ignoreFace, decision);
            if (zPast != null) decide["beat"] = "callback";
            if (scene != null) _snd.Attach(decide, scene, "z", _rng);
        }
        _log.Write(decide);
        if (!o.Followed && seq < 4 && _rng.NextDouble() < 0.3)
        {
            string retort = Line("m|retort", r, null);
            if (!string.IsNullOrEmpty(retort)) _log.Write(Entry(r, o.Exchange, seq, "retort", "m", retort, "tsun"));
        }
        _lastByQuestion[r.Question] = (o.Final, o.Relation);
        _mem.Save();
        return o;
    }

    /// <summary>判断の記録（学習の decisionId）に相談を結び付ける。結果が出たら Resolved で受け取る。</summary>
    public void Bind(string decisionId, ConsultRequest r, ConsultOutcome o, string horizon)
    {
        if (string.IsNullOrEmpty(decisionId) || o == null || o.Relation is "solo" or "unvoiced") return;
        _bound[decisionId] = new Bound
        {
            Exchange = o.Exchange, Question = r.Question, Relation = o.Relation, Followed = o.Followed,
            ZKey = r.Z.Key, MKey = r.M?.Key, Final = o.Final, ZLabel = r.Z.Label, MLabel = r.M?.Label, Day = r.Day, Horizon = horizon,
            MCollapse = r.MCollapse,
        };
        if (_bound.Count > 400) _bound.Remove(_bound.Keys.First());
    }

    /// <summary>結び付けた判断の決まった結果（PersonaStrategy.ResolvePending から）。</summary>
    public void Resolved(string decisionId, int reward)
    {
        if (decisionId == null || !_bound.TryGetValue(decisionId, out var b)) return;
        _bound.Remove(decisionId);
        _resolved.Add((b, reward));
    }

    /// <summary>まとめて決まった結果から学び、いちばん目立つ 1 件にだけ後日談の掛け合いを付ける。</summary>
    public void Flush(int day, string phase)
    {
        if (_resolved.Count == 0) return;
        (Bound b, int reward, string verdict, float diff)? best = null;
        var cands = new List<(Bound b, int reward, string verdict, float diff)>();
        foreach (var (b, reward) in _resolved)
        {
            // 良し悪しは「いつもの結果」との差で見る（配信は売上が入るのでいつもプラス、ランの増減もいつもプラスのため）
            int thr = Threshold(b.Horizon);
            float usual = _mem.Usual(b.Horizon);
            float diff = reward - usual;
            string verdict = diff >= thr ? "good" : diff <= -thr ? "bad" : "even";
            var stat = _mem.Advice(b.Question);
            if (b.Relation == "agree")
            {
                if (verdict == "good") stat.AgreeGood++;
                else if (verdict == "bad") stat.AgreeBad++;
                continue;
            }
            if (b.Followed)
            {
                if (verdict == "good") stat.FollowedGood++;
                else if (verdict == "bad") stat.FollowedBad++;
            }
            else
            {
                if (verdict == "good") stat.IgnoredGood++;
                else if (verdict == "bad") stat.IgnoredBad++;
            }
            // ずんだもんがゆっくり学ぶ: 聞いて得した・聞かずに損した → 信頼が上がる（逆なら下がる）
            int signal = verdict == "even" ? 0 : (b.Followed == (verdict == "good")) ? 1 : -1;
            _mem.AddTrust(Group(b.Question), signal * ConsultMemory.TrustStep);
            _zMem?.Append(new PersonaOutcome
            {
                Time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), Kind = "consult", Day = b.Day, GoldDelta = reward, Result = verdict,
                Choice = $"q={b.Question},zundamon={b.ZKey},metan={b.MKey},final={b.Final},followed={b.Followed}",
            }, null);
            _mMem?.Append(new PersonaOutcome
            {
                Time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), Kind = "advice", Day = b.Day, GoldDelta = reward, Result = verdict,
                Choice = $"q={b.Question},advice={b.MKey},zundamon={b.ZKey},final={b.Final},followed={b.Followed}",
            }, null);
            if (verdict == "even") continue;
            cands.Add((b, reward, verdict, diff));
        }
        // 後日談の 1 件（P5）: いちばん大きく動いたもの、ではなく種類の重み × 動いた大きさ（2 まで）× 気分で抽選する。
        // CHARACTERS.md: 「4 回に 1 回成功してマウント」（マウント : 泣き = 1:3〜1:5）。聞かずに当てた : 外した は素で 1:2 くらいなので、
        // マウントは 0.45、だから言ったでしょ 1.0、お礼 0.6、文句 0.5。泣いている最中（しょんぼり）はだから言ったでしょを少し控え、
        // ドヤの最中はマウントを重ねない（長くて 3 行、4 行目で天罰）。
        if (cands.Count > 0)
        {
            var weights = cands.Select(x =>
            {
                string k = x.b.Followed ? (x.verdict == "good" ? "thanks" : "blame") : (x.verdict == "good" ? "mount" : "told");
                double w = k switch { "mount" => 0.45, "told" => 1.0, "thanks" => 0.6, _ => 0.5 };
                if (k == "told" && _zMood.Sad >= PersonaMood.Threshold) w *= 0.7;
                if (k == "mount" && _zMood.Smug >= PersonaMood.Threshold) w *= 0.5;
                return w * Math.Min(2.0, Math.Abs(x.diff) / Math.Max(1, Threshold(x.b.Horizon)));
            }).ToList();
            double total = weights.Sum(), pick = _rng.NextDouble() * total;
            for (int i = 0; i < cands.Count; i++)
            {
                pick -= weights[i];
                if (pick <= 0 || i == cands.Count - 1)
                {
                    best = cands[i];
                    break;
                }
            }
        }
        foreach (var (b, reward) in _resolved) _mem.AddUsual(b.Horizon, reward);
        _resolved.Clear();
        // 後日談は 1 日に 1 回まで。聞いて当たった（お礼）はめたんがまっすぐ褒められる場面なので、半分は黙って流す
        if (best != null && day != _lastFollowUpDay)
        {
            var bv = best.Value;
            bool thanks = bv.b.Followed && bv.verdict == "good";
            if (!thanks || _rng.NextDouble() < 0.5)
            {
                _lastFollowUpDay = day;
                FollowUp(day, phase, bv.b, bv.reward, bv.verdict, bv.diff);
            }
        }
        _mem.Save();
    }

    private void FollowUp(int day, string phase, Bound b, int reward, string verdict, float diff)
    {
        bool good = verdict == "good";
        string winner = b.Followed ? (good ? "metan" : "zundamon") : (good ? "zundamon" : "metan");
        string kind = b.Followed ? (good ? "thanks" : "blame") : (good ? "mount" : "told");
        _mem.FollowUps++;
        var res = new Dictionary<string, object>
        {
            ["exchange"] = b.Exchange, ["question"] = b.Question, ["reward"] = reward, ["vsUsual"] = Mathf.RoundToInt(diff), ["verdict"] = verdict,
            ["followed"] = b.Followed, ["final"] = b.Final, ["winner"] = winner, ["horizon"] = b.Horizon,
        };
        var ctx = new Dictionary<string, string>
        {
            ["his"] = b.ZLabel ?? b.ZKey, ["mine"] = b.MLabel ?? b.MKey,
            // 金額: 勝ったときは実際の損益（プラスのとき）、負けたときは「いつもより」の差
            ["amount"] = Money(Mathf.RoundToInt(reward > 0 ? reward : Math.Abs(diff))), ["short"] = Money(Mathf.RoundToInt(Math.Abs(diff))),
        };
        int ex = ++_exchange;
        int seq = 0;
        // 気分: 押し切って当てた → ずんだもんはドヤ・めたんはイラ、押し切って外した → ずんだもんはしょんぼり（4 章）
        res["moodBefore"] = _zMood.Snapshot();
        res["moodBeforeM"] = _mMood.Snapshot();
        _zMood.OnFollowUp(kind, true);
        _mMood.OnFollowUp(kind, false);
        res["moodAfter"] = _zMood.Snapshot();
        res["moodAfterM"] = _mMood.Snapshot();
        if (kind is "told" or "mount") _events.Add((kind, Group(b.Question), b.ZLabel ?? b.ZKey, day));
        bool stream = b.Horizon == PersonaFeatures.HorizonStream;
        bool bigLoss = reward <= -BigLoss(b.Horizon);
        switch (kind)
        {
            case "told":
            {
                var told = FollowEntry(day, phase, ex, seq++, kind, "m", Fill("m|told", ctx), "tsun", res);
                _snd.Attach(told, "m_told", "m", _rng);
                _log.Write(told);
                var cry = FollowEntry(day, phase, ex, seq++, "cry", "z", Fill("z|cry", ctx), "namida", null);
                _snd.Attach(cry, bigLoss ? "z_cry_big" : stream ? "z_cry_stream" : "z_cry", "z", _rng);
                _log.Write(cry);
                if (_rng.NextDouble() < 0.5)
                {
                    string rec = Fill("z|recover", ctx);
                    if (!string.IsNullOrEmpty(rec)) _log.Write(FollowEntry(day, phase, ex, seq, "recover", "z", rec, "hero", null));
                }
                break;
            }
            case "mount":
            {
                var mount = FollowEntry(day, phase, ex, seq++, kind, "z", Fill("z|mount", ctx), "tsun", res);
                _snd.Attach(mount, "z_mount", "z", _rng);
                _log.Write(mount);
                string mr = Fill("m|mount_reply", ctx);
                if (!string.IsNullOrEmpty(mr))
                {
                    var reply = FollowEntry(day, phase, ex, seq, "mount_reply", "m", mr, "tsun", null);
                    _snd.Attach(reply, "m_mount_reply", "m", _rng);
                    _log.Write(reply);
                }
                break;
            }
            case "thanks":
            {
                _log.Write(FollowEntry(day, phase, ex, seq++, kind, "z", Fill("z|thanks", ctx), "hiso", res));
                // めたんのまっすぐな褒めは 1 ランに 1 回まで（山場）。それ以外は遠回し
                bool praise = !_praiseUsed && _rng.NextDouble() < 0.35;
                var reply = FollowEntry(day, phase, ex, seq, "thanks_reply", "m", Fill(praise ? "m|praise" : "m|thanks_reply", ctx), "ama", null);
                if (praise)
                {
                    _praiseUsed = true;
                    reply["beat"] = "praise";
                }
                _snd.Attach(reply, "m_right", "m", _rng);
                _log.Write(reply);
                break;
            }
            default:
                _log.Write(FollowEntry(day, phase, ex, seq++, kind, "z", Fill("z|blame", ctx), "surprise", res));
                if (b.MCollapse)
                {
                    // 崩れて外した: イラは消え、めたんは hero（「次は絶対当てる」で外した）
                    _mMood.EndCollapse();
                    var fail = FollowEntry(day, phase, ex, seq, "blame_reply", "m", Fill("m|collapse_fail", ctx), "hero", null);
                    fail["beat"] = "collapse_fail";
                    _log.Write(fail);
                }
                else _log.Write(FollowEntry(day, phase, ex, seq, "blame_reply", "m", Fill("m|blame_reply", ctx), "hiso", null));
                break;
        }
        // 教訓（英語、次のリクエストの state に載る）。目立った 1 件だけ（教訓が相談で埋まらないように）
        string q = QuestionEn(b.Question);
        string net = $"{reward:+#;-#;0}G";
        _zMem?.Append(new PersonaOutcome
        {
            Time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), Kind = "consult_lesson", Day = b.Day, GoldDelta = reward, Result = winner,
            Lesson = b.Followed
                ? $"Day {b.Day}: I listened to Metan on {q} and the result was {net}."
                : $"Day {b.Day}: I ignored Metan's advice on {q} and the result was {net}.",
        }, null);
        _mMem?.Append(new PersonaOutcome
        {
            Time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), Kind = "advice_lesson", Day = b.Day, GoldDelta = reward, Result = winner,
            Lesson = b.Followed
                ? $"Day {b.Day}: Zundamon followed my advice on {q}; the result was {net}."
                : $"Day {b.Day}: Zundamon ignored my advice on {q}; the result was {net}.",
        }, null);
    }

    // =====================================================
    // 配信中の山場（スパチャを投げる瞬間）
    // =====================================================

    public string InStream(int day, string zKey, string mKey, float zConf, float mConf, string zLabel, string mLabel, ConsultFacts f, string moment)
    {
        var r = new ConsultRequest
        {
            Question = "intervene_now", Phase = "in_stream", Day = day, Facts = f,
            Z = new ConsultPick { Key = zKey, Label = zLabel, Conf = zConf },
            M = new ConsultPick { Key = mKey, Label = mLabel, Conf = mConf },
            ZBand = PersonaLines.BandOf(zConf),
        };
        r.ZLine = Line("z|instream", r, null);
        var o = Exchange(r);
        return o.Final;
    }

    // =====================================================
    // 聞くかどうか
    // =====================================================

    /// <summary>
    /// ずんだもんが聞く確率（P5: 特性と気分から）。
    /// 基本 = 助言の受け入れ receptivity（0.30）+ 0.02（反対）/ + 0.25（条件つき）。
    /// + 0.3 ×（めたんの確信 − 0.5）− 0.3 ×（自分の確信 − 0.5）+ 0.6 × 信頼（質問の種類ごと）− 自信満々なら 0.08 + 返済が怖いとき 0.12
    /// 気分: 自信（基準点より上）とドヤで下がり（勝つと聞かない＝ハウスマネー）、しょんぼりで上がる。0.08〜0.8。
    /// 目標（M9）: 反対 0.30〜0.45、条件つき 0.50〜0.65。
    /// </summary>
    private float FollowProbability(ConsultRequest r, string relation)
    {
        float rec = _zMood.Traits.Receptivity;
        float p = relation == "condition" ? rec + 0.20f : rec + 0.02f;   // 条件つき +0.25 → +0.20（M9: 0.61〜0.70 → 目標 0.50〜0.65 の中ほど）
        p += 0.3f * (r.M.Conf - 0.5f) - 0.3f * (r.Z.Conf - 0.5f);
        p += 0.6f * _mem.Trust(Group(r.Question));
        if (r.ZBand == PersonaLines.Band.High) p -= 0.08f;   // 自信を持った瞬間は聞かない
        var f = r.Facts;
        if (f != null && f.DebtDaysLeft >= 0 && f.DebtDaysLeft <= 2 && f.Cash > 0 && f.DebtAmount > f.Cash / 2) p += 0.12f;   // 返済が怖いときは聞く
        p -= 0.25f * Mathf.Max(0f, _zMood.Conf - _zMood.ConfBase) + 0.3f * _zMood.Smug;
        p += 0.25f * _zMood.Sad;
        return Mathf.Clamp(p, 0.08f, 0.8f);
    }

    private static bool IsBigBet(ConsultRequest r)
    {
        int z = Level(r.Z.Key);
        return r.Question switch
        {
            "budget" => r.Levels && z >= 4,
            "intensity" => r.Levels && z >= 4,
            "support" => r.Levels && z >= 3,
            "village_budget" => r.Levels && z >= 4,
            "stream_side" => r.Z.Key != "none" && r.M.Key == "none",
            _ => false,
        };
    }

    private static string IgnoreScene(ConsultRequest r) => r.Question switch
    {
        "budget" when Level(r.Z.Key) >= 5 => "z_ignore_daijoubu",
        "support" => "z_ignore_daijoubu",
        "budget" => "z_ignore_power",
        "stream_side" => "z_ignore_bet",
        "intensity" => "z_ignore_bet",
        _ => null,
    };

    private static int Threshold(string horizon) => horizon switch
    {
        PersonaFeatures.HorizonNextDay => 3000,
        PersonaFeatures.HorizonAfterStream => 10000,
        PersonaFeatures.HorizonStream => 5000,
        PersonaFeatures.HorizonNextRun => 50000,
        _ => 5000,
    };

    private static int BigLoss(string horizon) => horizon == PersonaFeatures.HorizonNextRun ? 300000 : 30000;

    public static string Group(string question) =>
        question.StartsWith("invest_", StringComparison.Ordinal) || question == "village_budget" ? "village"
        : question.StartsWith("intervene", StringComparison.Ordinal) ? "intervene" : question;

    private static int Level(string key) => int.TryParse(key, out var v) ? v : 0;

    // =====================================================
    // セリフ
    // =====================================================

    private string Line(string key, ConsultRequest r, string reason, string past = null)
    {
        var ctx = new Dictionary<string, string>
        {
            ["his"] = r.Z.Label ?? r.Z.Key,
            ["mine"] = r.M?.Label ?? r.M?.Key ?? "",
            ["reason"] = reason ?? "",
            ["past"] = past ?? "",
        };
        return Fill(key, ctx);
    }

    /// <summary>言い過ぎるなら黙ってよい掛け合い（あいづち・捨て台詞・立ち直り）。間隔の決まりを満たす行が無ければ空を返す。</summary>
    private static readonly HashSet<string> Quietable = new() { "m|ack", "m|agree", "m|retort", "z|recover", "m|mount_reply" };

    private string Fill(string key, Dictionary<string, string> ctx)
    {
        if (!ConsultLines.Templates.TryGetValue(key, out var list) || list.Length == 0) return "";
        if (!_recent.TryGetValue(key, out var recent)) _recent[key] = recent = new List<string>();
        var pool = list.Where(l => !recent.Contains(l)).ToList();
        if (pool.Count == 0) pool = list.ToList();
        // 理由が空なら {reason} の無い行を選ぶ
        if (ctx.TryGetValue("reason", out var rs) && string.IsNullOrEmpty(rs))
        {
            var noReason = pool.Where(l => !l.Contains("{reason}")).ToList();
            if (noReason.Count > 0) pool = noReason;
        }
        string Render(string t)
        {
            string l = t;
            foreach (var kv in ctx) l = l.Replace("{" + kv.Key + "}", kv.Value ?? "");
            return l.Replace("？ 。", "？ ").Replace("。。", "。").Replace("、。", "。").Replace(" 。", "。");
        }
        // 一言の間隔（P6・M12）: 同じ行は 1 ラン 3 回まで・20 行あける（ずんだもんの判断の一言と同じ数え方）
        if (_memes != null)
        {
            var strict = pool.Where(t => _memes.Fresh(Render(t))).ToList();
            if (strict.Count == 0) strict = list.Where(t => _memes.Fresh(Render(t))).ToList();
            // あいづちは黙る。ミーム音つきの押し切り（行が 1〜2 本しかない）は、ふつうの押し切りに戻す（呼び出し側）
            if (strict.Count == 0 && (Quietable.Contains(key) || key.StartsWith("z|ignore_", StringComparison.Ordinal))) return "";
            if (strict.Count == 0) strict = list.Where(t => _memes.UnderCap(Render(t))).ToList();
            if (strict.Count == 0)
            {
                int min = list.Min(t => _memes.CountOf(Render(t)));
                strict = list.Where(t => _memes.CountOf(Render(t)) == min).ToList();
            }
            pool = strict;
        }
        string tpl = pool[_rng.Next(pool.Count)];
        recent.Add(tpl);
        while (recent.Count > Math.Max(1, list.Length / 2)) recent.RemoveAt(0);
        string line = Render(tpl);
        if (!PersonaLines.IsClean(line)) line = key.StartsWith("m|", StringComparison.Ordinal) ? "それだけ。" : "いくのだ！";
        _memes?.Use(line, key.StartsWith("m|", StringComparison.Ordinal) ? "metan" : "zundamon");
        return line;
    }

    /// <summary>めたんの理由（画面の数字から。support = 賛成の理由か）。無ければ空。</summary>
    private static string Reason(ConsultRequest r, string mine, bool support)
    {
        var f = r.Facts ?? new ConsultFacts();
        string q = r.Question;
        int mineLv = Level(mine), hisLv = Level(r.Z.Key);
        bool lower = r.Levels && mineLv < hisLv;
        bool higher = r.Levels && mineLv > hisLv;
        switch (q)
        {
            case "budget":
                if ((lower || support) && f.DebtDaysLeft >= 0 && f.DebtDaysLeft <= 4 && f.DebtAmount > 0)
                    return $"返済まであと{f.DebtDaysLeft}日、{Money(f.DebtAmount)}要るのよ";
                if (lower && f.Cash > 0) return $"手持ちは{Money(f.Cash)}。全部は張らないの";
                if (higher && f.Cash > 0) return $"手持ち{Money(f.Cash)}を遊ばせておくのはもったいないわ";
                if (f.StreamDays == 0 && f.WeaknessKnown) return "明日は配信、弱点も分かってるわ";
                return f.Cash > 0 ? $"手持ちは{Money(f.Cash)}よ" : "";
            case "buy_style":
                return mine switch
                {
                    "dungeon_focus" when f.WeaknessKnown && !string.IsNullOrEmpty(f.Weakness) => $"次の配信の弱点は{AttributeJa(f.Weakness)}よ",
                    "recommend" => "迷ったら期待値の高い順よ",
                    "bargain" => "安く買って高く売るのが基本",
                    "dividend" => "配当は毎日入るの",
                    _ => "",
                };
            case "newspaper":
                return mine == "true" ? "新聞は相場の答え合わせよ" : "";
            case "intel":
                if (mine == "true")
                    return f.IntelCost > 0 ? $"弱点を知らずに仕入れるのは賭けよ。情報料は{Money(f.IntelCost)}" : "弱点を知らずに仕入れるのは賭けよ";
                if (f.IntelCost > 0 && f.Cash > 0) return $"情報料{Money(f.IntelCost)}、手持ち{Money(f.Cash)}から出すのよ";
                return f.StreamDays >= 2 ? $"配信まであと{f.StreamDays}日。買うのはまだ早いわ" : "";
            case "support":
                if (mineLv == 0 && f.SupportCost > 0) return $"支援の{Money(f.SupportCost)}は勇者が勝てば丸損よ";
                if (mineLv > 0 && f.SupportCost > 0 && f.RewardAfterSupport > f.DefeatReward)
                    return $"{Money(f.SupportCost)}で防衛報酬が{Money(f.RewardAfterSupport - f.DefeatReward)}増えるわ";
                return "";
            case "focus_share":
                if (lower) return "ダンジョン向けに寄せすぎると、ふだんの棚が空くわ";
                if (higher) return "弱点の武器は配信でよく売れるわ";
                return f.WeaknessKnown && !string.IsNullOrEmpty(f.Weakness) ? $"弱点は{AttributeJa(f.Weakness)}よ" : "";
            case "stream_side":
                if (mine == "hero")
                    return f.WeaknessKnown && !string.IsNullOrEmpty(f.Weakness) ? $"弱点は{AttributeJa(f.Weakness)}、武器も揃えたわ" : "勇者の方が分がいいわ";
                if (mine == "demon") return f.SupportPaid ? "魔王軍に出資したんだから筋を通しなさい" : "このダンジョン、勇者には荷が重いわ";
                if (!f.WeaknessKnown) return "弱点も知らずに張るのは博打よ";
                return "見てるだけでも売上は入るの";
            case "intensity":
                if (lower && f.SpendBudget > 0) return $"スパチャの予算は{Money(f.SpendBudget)}。全部は使わないの";
                if (lower) return "投げすぎると利益が消えるわ";
                if (higher) return f.Side == "hero" ? "勇者に張ったなら背中を押す場面よ" : "張ったなら少しは押さないと";
                return "";
            case "village_budget":
                // 段階は「使う割合」。残す額と、元が取れる施設に要る額を言う（「4分の1は残す」と言って 25% を勧める取り違えをしない）
                if (lower && f.Needed > 0 && !string.IsNullOrEmpty(f.NeededNames))
                    return f.Reserve > 0
                        ? $"{f.NeededNames}なら{Money(f.Needed)}で足りるわ。{Money(f.Reserve)}は残すの"
                        : $"{f.NeededNames}なら{Money(f.Needed)}で足りるわ";
                if (lower && mineLv == 0) return "いま元が取れる施設は無いわ";
                if (lower && f.Funds > 0 && f.Reserve > 0) return $"村資金{Money(f.Funds)}のうち{Money(f.Reserve)}は残すの";
                if (higher && f.Needed > 0 && !string.IsNullOrEmpty(f.NeededNames)) return $"{f.NeededNames}に{Money(f.Needed)}要るのよ";
                if (higher && f.Funds > 0) return $"村資金{Money(f.Funds)}を寝かせても1Gも増えないわ";
                return "";
            case "shop_upgrade":
            case "smith_upgrade":
            case "broker_upgrade":
            {
                string what = f.FacilityName ?? "それ";
                if (mine == "true")
                    return f.Cost > 0 && f.Cash > 0 ? $"{what}は{Money(f.Cost)}、手持ち{Money(f.Cash)}なら払えるわ。ランの終わりまで残るもの" : $"{what}はランの終わりまで残るの";
                if (f.DebtDaysLeft >= 0 && f.DebtDaysLeft <= 3 && f.DebtAmount > 0) return $"返済まであと{f.DebtDaysLeft}日、{Money(f.DebtAmount)}要るのよ";
                return f.Cost > 0 ? $"{what}に{Money(f.Cost)}は今は重いわ" : "";
            }
            case "machine":
                if (mine == "none")
                    return f.DebtDaysLeft >= 0 && f.DebtDaysLeft <= 3 && f.DebtAmount > 0 ? $"返済まであと{f.DebtDaysLeft}日よ" : "設備は置いたら最後まで外せないのと同じよ";
                if (f.PaybackDays > 0) return $"{f.FacilityName ?? "それ"}なら{f.PaybackDays}日で元が取れるわ";
                return f.Cost > 0 ? $"{f.FacilityName ?? "それ"}は{Money(f.Cost)}。毎日働くのよ" : "";
            case "finance":
                if (mine == "none") return f.Cash > 0 ? $"手持ち{Money(f.Cash)}は仕入れに回すの" : "";
                if (mine.StartsWith("sell_", StringComparison.Ordinal)) return "利が乗ったうちに売るの";
                return mine.StartsWith("bond", StringComparison.Ordinal) ? "債券なら満期に利息が付くわ。減らないの" : "ファンドは相場次第よ。減ることもあるわ";
            case "finance_share":
                if (lower) return f.Reserve > 0 ? $"{Money(f.Reserve)}は残すの" : "入れすぎると仕入れができないわ";
                if (higher) return "寝かせておくより増えるわ";
                return "";
            case "hero_weapon":
            case "hero_armor":
                if (f.WeaknessKnown && !string.IsNullOrEmpty(f.Weakness) && support) return $"弱点は{AttributeJa(f.Weakness)}よ";
                return !string.IsNullOrEmpty(f.ItemName) ? $"{f.ItemName}なら在庫も値上がりするわ" : "";
            case "hero_tactics":
                return mine == "Aggressive" ? "攻めるなら武器の値が動くわ" : "値動きは荒らさない方がいいわ";
            case "starter_relic":
            {
                // 手堅いもの（毎朝のお金・仕入れ値・納付金）は最初の日から積み上がる、派手なものは当たれば大きい
                string name = string.IsNullOrEmpty(r.M?.Label) ? "それ" : r.M.Label;
                if (PersonaRequests.SteadyRelics.Contains(mine)) return $"{name}なら最初の営業日から毎日積み上がるわ";
                if (PersonaRequests.FlashyRelics.Contains(mine)) return support ? "当たれば大きいわね" : $"{name}は当たれば大きいわ";
                return $"{name}は店の地力が上がるもの";
            }
            case "intervene":
                return mine is "heal" or "skill" or "special"
                    ? (f.Side == "demon" ? "" : "勇者を助ける方が筋よ")
                    : f.Side == "hero" ? "" : "勇者の足を止める方が筋よ";
            case "intervene_now":
                if (f.HeroHpPct == 0) f.HeroHpPct = -1;   // 0% は読めていない（倒れた勇者には投げない）
                if (mine == "none")
                    return f.HeroHpPct >= 0 ? HeroHpJa(f.HeroHpPct) + "よ" : f.SpendBudget > 0 ? $"スパチャの予算は残り{Money(f.SpendBudget)}よ" : "";
                return f.HeroHpPct >= 0 ? HeroHpJa(f.HeroHpPct) : "";
            default:
                if (q.StartsWith("invest_", StringComparison.Ordinal))
                {
                    string fac = f.FacilityName ?? "その施設";
                    if (mine == "true" && f.Cost > 0 && f.Funds > 0)
                        return f.Cost <= f.Funds * 0.05f
                            ? $"{fac}は{Money(f.Cost)}、村資金{Money(f.Funds)}からすればちょっとよ"
                            : $"{fac}は{Money(f.Cost)}。前のランの儲けで取り返せるわ";
                    if (mine == "false" && f.Cost > 0) return $"{fac}に{Money(f.Cost)}は重いわ";
                }
                return "";
        }
    }

    /// <summary>勇者の HP を割合ではなく言葉で（画面の HP バーの見た目と合う言い方）。後ろに「よ」を付けて使う。</summary>
    private static string HeroHpJa(int pct) =>
        pct >= 75 ? "勇者はまだぴんぴんしてる" :
        pct >= 50 ? "勇者のHPはまだ半分以上ある" :
        pct >= 25 ? "勇者のHPはもう半分を切ってる" :
        "勇者はもう虫の息";

    public static string Money(int g)
    {
        long v = Math.Abs((long)g);
        string sign = g < 0 ? "マイナス" : "";
        if (v >= 100_000_000) return sign + (v / 100_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "億G";
        if (v >= 10_000) return sign + (v / 10_000.0).ToString(v >= 1_000_000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "万G";
        return sign + v.ToString("N0", CultureInfo.InvariantCulture) + "G";
    }

    private static string AttributeJa(string attr) => attr switch
    {
        "Fire" => "炎",
        "Water" => "水",
        "Wind" => "風",
        "Earth" => "土",
        "Light" => "光",
        "Dark" => "闇",
        "Thunder" => "雷",
        "Ice" => "氷",
        _ => attr,
    };

    private static string QuestionEn(string q) => q switch
    {
        "budget" => "the shopping budget",
        "buy_style" => "how to buy",
        "newspaper" => "reading the newspaper",
        "intel" => "buying the dungeon information",
        "support" => "paying the demon army",
        "focus_share" => "goods for the stream",
        "stream_side" => "which side to back",
        "intensity" => "how often to pay super chats",
        "village_budget" => "the village budget",
        "shop_upgrade" => "remodeling the shop",
        "smith_upgrade" => "developing the blacksmith",
        "broker_upgrade" => "developing the information broker",
        "machine" => "placing a machine",
        "finance" => "the exchange",
        "finance_share" => "how much to invest",
        "hero_weapon" => "the hero's weapon",
        "hero_armor" => "the hero's armor",
        "hero_tactics" => "the hero's tactics",
        "starter_relic" => "the starter relic",
        _ => q.StartsWith("invest_", StringComparison.Ordinal) ? "investing in the " + q.Substring(7) : q,
    };

    // =====================================================
    // ログの行
    // =====================================================

    private static Dictionary<string, object> PickJson(ConsultPick p)
    {
        if (p == null) return null;
        var d = new Dictionary<string, object> { ["key"] = p.Key, ["label"] = p.Label, ["conf"] = Math.Round(p.Conf, 3) };
        if (p.Probs != null) d["probs"] = p.Probs.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 4));
        if (p.Why != null) d["why"] = p.Why;
        if (p.LatencyMs.HasValue) d["latencyMs"] = p.LatencyMs.Value;
        return d;
    }

    private static Dictionary<string, object> Entry(ConsultRequest r, int exchange, int seq, string kind, string speaker, string line, string emotion,
        Dictionary<string, object> decision = null)
    {
        var d = Base(r.Day, r.Phase, exchange, seq, kind, speaker, line, emotion);
        d["question"] = r.Question;
        if (decision != null) d["decision"] = decision;
        return d;
    }

    private static Dictionary<string, object> FollowEntry(int day, string phase, int exchange, int seq, string kind, string speaker, string line,
        string emotion, Dictionary<string, object> result)
    {
        var d = Base(day, phase, exchange, seq, kind, speaker, line, emotion);
        if (result != null) d["result"] = result;
        return d;
    }

    private static Dictionary<string, object> Base(int day, string phase, int exchange, int seq, string kind, string speaker, string line, string emotion)
    {
        var now = DateTimeOffset.Now;
        var d = new Dictionary<string, object>
        {
            ["time"] = now.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
            ["unixMs"] = now.ToUnixTimeMilliseconds(),
            ["run"] = PersonaLog.RunTag,
            ["day"] = day,
            ["phase"] = phase,
            ["gamePhase"] = AutoPlay.CurrentPhaseLabel,
            ["timeScale"] = Math.Round(Time.timeScale, 2),
            ["exchange"] = exchange,
            ["seq"] = seq,
            ["kind"] = kind,
        };
        if (speaker != null)
        {
            d["speaker"] = speaker;
            d["line"] = line ?? "";
            d["e"] = emotion ?? "normal";
        }
        return d;
    }
}

/// <summary>相談の掛け合いのテンプレート（CHARACTERS.md の型。{his} = ずんだもんの案、{mine} = めたんの案、{reason} = 画面の数字の理由）。</summary>
public static class ConsultLines
{
    public static readonly Dictionary<string, string[]> Templates = new()
    {
        ["m|ack"] = new[]
        {
            "いいわ。", "それで。", "異議なし。", "……まあ、いいわ。", "同じ意見よ。", "珍しく合うわね。",
            "正解。", "それでいいの。", "今回は止めないわ。", "ええ。", "悪くないわ。", "私もそう思ってた。",
        },
        ["m|agree"] = new[]
        {
            "{his}でいいわ。{reason}。",
            "賛成。{reason}。",
            "珍しく同じ意見ね。{his}で。",
            "それなら文句はないわ。{reason}。",
            "いいんじゃない。{reason}。",
            "{his}で。私でも1万は残すけど。",
            "今回は計算が合うわ。{reason}。",
        },
        ["m|disagree"] = new[]
        {
            "{his}はやめて。{reason}。{mine}にしなさい。",
            "逆。{reason}。{mine}よ。",
            "待って。{reason}。私なら{mine}。",
            "{his}？ {reason}。{mine}にしておきなさい。",
            "反対。{reason}。だから{mine}。",
            "はい問題。{reason}。答えは{mine}よ。",
            "言い方を変えるわ。{reason}。{mine}。",
        },
        // 呼び戻し（P6）: 同じ種類の質問で前に聞かずに外した（{past} = そのときのずんだもんの案）。「回目」は使わない
        ["m|disagree_callback"] = new[]
        {
            "この前も{past}で外したでしょ。{mine}よ。",
            "{past}で泣いたの、もう忘れたの？ {mine}にしなさい。",
            "前の{past}を思い出して。{mine}よ。",
            "これで何度目かしら。{mine}にしなさい。",
            "数えてるからね。{past}のときと同じよ。{mine}。",
        },
        // 金欠の事実（CHARACTERS.md: 常に金欠）。大きな賭けへの反対
        ["m|disagree_money"] = new[]
        {
            "それ、私の3か月分のお小遣いよ。{mine}にして。",
            "缶ジュース何本分だと思ってるの。{mine}よ。",
            "私なら1万は残す。{mine}にしなさい。",
        },
        ["m|condition"] = new[]
        {
            "{his}でもいいけど、{reason}。{mine}にしておけば安全よ。",
            "悪くはないわ。でも{reason}。{mine}くらいが無難。",
            "半分賛成。{reason}。{mine}なら乗るわ。",
            "{his}……{reason}。せめて{mine}。",
            "言いたいことは分かるわ。{mine}にしておきなさい。",
        },
        ["m|instream_wait"] = new[] { "まだ早いわ。{reason}。", "今は投げないで。{reason}。", "待って。{reason}。様子見よ。", "まだ早いわ。様子見。" },
        ["m|instream_other"] = new[] { "{his}より{mine}。{reason}よ。", "投げるなら{mine}よ。{reason}だもの。", "{his}より{mine}にしなさい。" },
        ["m|retort"] = new[] { "言ったからね。", "知らないわよ。", "……数えてるからね。", "私は止めたわ。", "あとで泣いても知らない。", "言い方。", "それだけ。" },
        ["z|follow"] = new[]
        {
            "……わかったのだ。{mine}にするのだ。",
            "しょうがないのだ。今回だけ{mine}なのだ。",
            "めたんがそこまで言うなら{mine}なのだ。……聞いてるのだ、ちゃんと。",
            "うー、{mine}なのだ。勘はお昼寝させておくのだ。",
            "{mine}にしておくのだ。学習したのだ。",
        },
        ["z|ignore"] = new[]
        {
            "聞いてるのだ。でも{his}なのだ！",
            "ぼくの勘は無料なのだ。{his}でいくのだ。",
            "めたんは慎重すぎて儲からないのだ。{his}なのだ。",
            "{his}なのだ！ 名前が強そうなのだ。",
            "数字より勘なのだ。{his}でいくのだ。",
            "ずんだ餅の精が{his}って言ってるのだ。",
            "77 はラッキーナンバーなのだ。{his}なのだ。",
            "コメントで 3 人が{his}って言ってたのだ。",
        },
        // 呼び戻し（P6）: 前に聞かずに当てた / 外した（{past} = そのときの案）
        ["z|ignore_mount_callback"] = new[]
        {
            "前も{past}で当てたのだ。{his}なのだ！",
            "前の{past}、覚えてるのだ？ {his}でいくのだ。",
            "ぼくの勘は実績があるのだ。{his}なのだ。",
        },
        ["z|ignore_told_callback"] = new[]
        {
            "前の{past}は忘れたのだ。{his}なのだ！",
            "前のは練習なのだ。{his}でいくのだ。",
            "{past}のことはもう泣いたから大丈夫なのだ。{his}なのだ。",
        },
        // ミーム音を付ける無茶な賭け（音の意味に合う言い方にする: index.json の usage.fit）
        // 「大丈夫だ、問題ない」の音に合う言い方を 6 通り（1 通りだと 1 ランに 6 回同じ行になった: M12）
        ["z|ignore_daijoubu"] = new[]
        {
            "大丈夫なのだ、問題ないのだ。{his}でいくのだ！",
            "問題ないのだ。{his}で大丈夫なのだ！",
            "{his}でいくのだ。大丈夫、問題ないのだ。",
            "心配いらないのだ。{his}で問題ないのだ！",
            "大丈夫なのだ。{his}、一番いいのを頼むのだ！",
            "{his}なのだ。ぼくは大丈夫、問題ないのだ。",
        },
        ["z|ignore_bet"] = new[] { "やるのだ！ {his}に乗るのだ！", "決めたのだ。{his}に張るのだ！" },
        ["z|ignore_power"] = new[] { "力押しなのだ！ {his}でいくのだ！", "細かいことは力で押し切るのだ。{his}なのだ！" },
        ["z|instream"] = new[] { "{his}を投げるのだ！", "ここで{his}なのだ！", "今なのだ、{his}！" },
        ["m|told"] = new[] { "だから言ったでしょ。", "ほら。いつもより{short}少ないわ。", "言ったわよね、{mine}って。", "……だから言ったのに。", "はい、{short}の授業料。" },
        ["z|cry"] = new[] { "うわーん、なのだ。", "ぼくの{his}が……。", "トコちゃん、ごめんなのだ。", "めたんの言う通りだったのだ……。", "ぼくの77個……なのだ。", "惜しかったのだ……。" },
        ["z|recover"] = new[] { "でも次は当たるのだ。", "次は勘が冴えるのだ。", "今のは練習なのだ。", "赤字の幅が減ったのは成長なのだ。", "学習したのだ。" },
        ["z|mount"] = new[]
        {
            "めたん、見たのだ？ {his}で{amount}なのだ。",
            "ぼくの勘が勝ったのだ！",
            "説明書より勘なのだ。",
            "めたんは慎重すぎて儲からないのだ。{amount}なのだ。",
            "録音したのだ。{his}で正解なのだ。",
        },
        ["m|mount_reply"] = new[] { "次はあなたに返ってくるからね。", "……たまたまよ。", "一回当たっただけ。", "数字は覚えておくわ。", "……次は絶対当てるわ。", "言い方。" },
        ["z|thanks"] = new[]
        {
            "……今回はめたんのおかげ、ということにしておくのだ。",
            "めたんの言う通りにしたら{amount}増えたのだ。……ちょっとだけ感謝なのだ。",
            "ま、まあ、ぼくが聞いてあげたからなのだ。",
        },
        ["m|thanks_reply"] = new[] { "それは認めるわ。", "当然よ。", "前回がひどすぎただけ。", "……悪くないわ。" },
        // めたんのまっすぐな褒め（1 ランに 1 回まで）
        ["m|praise"] = new[] { "……悪くない判断だったわ、あなたも。", "今のは、ちゃんと聞いたあなたの手柄よ。" },
        ["z|blame"] = new[] { "めたんの言う通りにしたのに！ いつもより{short}少ないのだ。", "めたんの読み、外れたのだ。", "{mine}にしたのに、なのだ……。" },
        ["m|collapse_fail"] = new[] { "……次は絶対当てるわ。", "今のは数えないで。", "……慎重にやればよかったわ。" },
        ["m|blame_reply"] = new[] { "……計算は合ってたのよ。", "相場が悪いの。私じゃない。", "……次は当てるわ。", "……説明書は読んだわよ。表紙は。", "単位は G。誤差の範囲よ。" },
    };
}

/// <summary>相談の記録（UserSettings/PersonaMemory/consult.json）: ずんだもんの「めたんを聞くと得か」、めたんの助言の成績。</summary>
public sealed class ConsultMemory
{
    public const float TrustStep = 0.04f;
    public const float TrustCap = 0.25f;

    public sealed class AdviceStat
    {
        [JsonProperty("asked")] public int Asked;
        [JsonProperty("agreed")] public int Agreed;
        [JsonProperty("followed")] public int Followed;
        [JsonProperty("ignored")] public int Ignored;
        [JsonProperty("followedGood")] public int FollowedGood;
        [JsonProperty("followedBad")] public int FollowedBad;
        [JsonProperty("ignoredGood")] public int IgnoredGood;
        [JsonProperty("ignoredBad")] public int IgnoredBad;
        [JsonProperty("agreeGood")] public int AgreeGood;
        [JsonProperty("agreeBad")] public int AgreeBad;
        /// <summary>めたんの助言が正しかった割合（聞いて当たった + 聞かずに外れた）/ 決着のついた反対・条件つき。</summary>
        [JsonIgnore] public float Accuracy
        {
            get
            {
                int n = FollowedGood + FollowedBad + IgnoredGood + IgnoredBad;
                return n > 0 ? (FollowedGood + IgnoredBad) / (float)n : 0.5f;
            }
        }
    }

    [JsonProperty("trust")] public Dictionary<string, float> TrustByGroup = new();
    [JsonProperty("advice")] public Dictionary<string, AdviceStat> AdviceByQuestion = new();
    [JsonProperty("followUps")] public int FollowUps;
    /// <summary>結果の付け方ごとの「いつもの結果」（G、指数移動平均）と件数。後日談の良し悪しはこれとの差で見る。</summary>
    [JsonProperty("usual")] public Dictionary<string, float> UsualByHorizon = new();
    [JsonProperty("usualCount")] public Dictionary<string, int> UsualCount = new();

    public float Usual(string horizon) =>
        horizon != null && UsualCount.TryGetValue(horizon, out var n) && n >= 3 && UsualByHorizon.TryGetValue(horizon, out var v) ? v : 0f;

    public void AddUsual(string horizon, int reward)
    {
        if (horizon == null) return;
        int n = UsualCount.TryGetValue(horizon, out var c) ? c : 0;
        float v = UsualByHorizon.TryGetValue(horizon, out var u) ? u : 0f;
        float a = Mathf.Max(0.1f, 1f / (n + 1));   // 最初は平均、慣れたら 0.1 の指数移動平均
        UsualByHorizon[horizon] = v + a * (reward - v);
        UsualCount[horizon] = n + 1;
    }
    [JsonIgnore] private string _path;

    public static string PathFor() => Path.Combine(PersonaPlayConfig.ProjectRoot, "UserSettings", "PersonaMemory", "consult.json");

    public static ConsultMemory Load()
    {
        string path = PathFor();
        ConsultMemory m = null;
        try
        {
            if (File.Exists(path)) m = JsonConvert.DeserializeObject<ConsultMemory>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 相談の記録を読めないので空から始めます: {path}: {e.Message}");
        }
        m ??= new ConsultMemory();
        m.TrustByGroup ??= new Dictionary<string, float>();
        m.AdviceByQuestion ??= new Dictionary<string, AdviceStat>();
        m.UsualByHorizon ??= new Dictionary<string, float>();
        m.UsualCount ??= new Dictionary<string, int>();
        m._path = path;
        return m;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 相談の記録を書けませんでした: {_path}: {e.Message}");
        }
    }

    public AdviceStat Advice(string question)
    {
        if (!AdviceByQuestion.TryGetValue(question, out var s)) AdviceByQuestion[question] = s = new AdviceStat();
        return s;
    }

    public float Trust(string group) => TrustByGroup.TryGetValue(group, out var t) ? t : 0f;

    public void AddTrust(string group, float delta)
    {
        if (delta == 0f) return;
        TrustByGroup[group] = Mathf.Clamp(Trust(group) + delta, -TrustCap, TrustCap);
    }

    public string TrustSummary() =>
        TrustByGroup.Count == 0 ? "まだ無し" : string.Join(" ", TrustByGroup.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value:+0.00;-0.00;0}"));
}

/// <summary>相談の会話ログ（Recordings/consult_log_&lt;ラン ID または日時&gt;.jsonl、1 行 = 1 セリフか 1 判断）。</summary>
public sealed class ConsultLog
{
    public string FilePath { get; }
    private static readonly JsonSerializerSettings Settings = new() { NullValueHandling = NullValueHandling.Ignore };

    public ConsultLog()
    {
        string dir = Path.Combine(PersonaPlayConfig.ProjectRoot, "Recordings");
        string tag = string.IsNullOrEmpty(PersonaLog.RunTag) ? DateTime.Now.ToString("yyyyMMdd_HHmmss") : Safe(PersonaLog.RunTag);
        FilePath = Path.Combine(dir, $"consult_log_{tag}.jsonl");
    }

    private static string Safe(string tag)
    {
        var sb = new StringBuilder();
        foreach (char ch in tag) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
        return sb.ToString();
    }

    public void Write(Dictionary<string, object> entry)
    {
        if (entry == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.AppendAllText(FilePath, JsonConvert.SerializeObject(entry, Settings) + "\n", new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 相談ログを書けませんでした: {FilePath}: {e.Message}");
        }
    }
}

/// <summary>
/// ミーム音（tools/jikkyo_video/assets/sounds_meme/index.json）。場面ごとの候補は、index.json の usage.fit / avoid を読んで
/// 合うと確かめたものだけをここに書く。読み込み時にも use・verified・who（話し手）を確かめ、合わないものは使わない。
/// </summary>
public sealed class ConsultSounds
{
    public const string IndexRelativePath = "tools/jikkyo_video/assets/sounds_meme/index.json";
    public const double Chance = 0.4;
    public const int MaxPerRun = 8;

    /// <summary>場面 → (音の id, 誰の一言に付けるか z / m)。各 id の fit を括弧に写す。</summary>
    public static readonly Dictionary<string, (string id, string who)[]> Scenes = new()
    {
        // めたんの反対（「めたんがずんだもんの理屈に反論する一言の頭」/「めたんがずんだもんの話を疑う一言のあと」）
        ["m_disagree"] = new[] { ("snd_igi_ari", "m"), ("snd_gorori_hontokanaa", "m") },
        // 全額級の賭けへの反対（「ずんだもんが全額賭け・無茶な補充を宣言した直後、めたんの呆れに重ねる」）
        ["m_disagree_big"] = new[] { ("snd_are_you_out_of_your_mind", "m"), ("snd_igi_ari", "m") },
        // 聞かずに全額（「ずんだもんが無謀な全額賭けを『大丈夫』と言い切った直後」。セリフに「大丈夫」がある z|ignore_daijoubu だけ）
        ["z_ignore_daijoubu"] = new[] { ("snd_daijoubu_mondainai", "z") },
        // 聞かずに張る（「ずんだもんが賭けに乗ると宣言した直後」）
        ["z_ignore_bet"] = new[] { ("snd_hikakin_yarimashou", "z") },
        // 聞かずに大きく仕入れる（「力押しの作戦を宣言した直後」。セリフが「力押し」/「力で押し切る」の z|ignore_power だけ）
        ["z_ignore_power"] = new[] { ("snd_kinnikun_power", "z") },
        // 聞かずに外して泣く: 配信（「ずんだもんの賭けが外れたのが分かった直後」/「読みが外れた・逆転された直後」）
        ["z_cry_stream"] = new[] { ("snd_faaah", "z"), ("snd_chotto_matte_uso_desho", "z") },
        // 同: 営業日（「ずんだもんがやらかしに気づいた直後」）、大損（「大損した瞬間のずんだもん」）
        ["z_cry"] = new[] { ("snd_e_uso_daro", "z") },
        ["z_cry_big"] = new[] { ("snd_tom_scream", "z") },
        // だから言ったでしょ（「めたんがずんだもんのひどい結果に突っ込む」/「失敗にめたんが呆れる」/「やらかしに、めたんがツッコむ」）
        ["m_told"] = new[] { ("snd_kore_wa_hidoi", "m"), ("snd_bruh", "m"), ("snd_naani_yattendaomae", "m") },
        // マウント（「ずんだもんが勝ってめたんにマウントを取る一言のあと」/「ずんだもんの作戦がうまくいった直後」）
        ["z_mount"] = new[] { ("snd_kuyashikunaino", "z"), ("snd_tere_ttere", "z") },
        // マウントへの返し（「めたんがずんだもんのドヤ顔に冷たく返す」）
        ["m_mount_reply"] = new[] { ("snd_kao_ga_akanwa", "m") },
        // 聞いて当たった（「読みが当たった直後」）
        ["m_right"] = new[] { ("snd_seikai", "m"), ("snd_correct_answer", "m") },
    };

    private sealed class Info
    {
        public string Timing;
        public float Duration;
    }

    private readonly Dictionary<string, Info> _usable = new();
    private readonly HashSet<string> _usedThisRun = new();
    private int _count;
    private int _sinceLast = 99;

    public int Usable => _usable.Count;

    public static ConsultSounds Load()
    {
        var s = new ConsultSounds();
        string path = Path.Combine(PersonaPlayConfig.ProjectRoot, IndexRelativePath);
        try
        {
            if (!File.Exists(path)) return s;
            var arr = JArray.Parse(File.ReadAllText(path));
            var byId = new Dictionary<string, JObject>();
            foreach (var t in arr)
                if (t is JObject o && o["id"] != null) byId[(string)o["id"]] = o;
            foreach (var scene in Scenes)
            {
                foreach (var (id, who) in scene.Value)
                {
                    if (!byId.TryGetValue(id, out var o)) continue;
                    if (o["use"]?.Type != JTokenType.Boolean || !(bool)o["use"]) continue;
                    if (o["verified"]?.Type != JTokenType.Boolean || !(bool)o["verified"]) continue;
                    string w = (string)o["who"];
                    string speaker = who == "z" ? "zundamon" : "metan";
                    if (w != "either" && w != speaker) continue;
                    string timing = (string)o["timing"];
                    if (timing is not ("before" or "after" or "over")) continue;
                    s._usable[id] = new Info { Timing = timing, Duration = o["duration"] != null ? (float)o["duration"] : 1f };
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] ミーム音の索引を読めないので音なしで続けます: {path}: {e.Message}");
            s._usable.Clear();
        }
        return s;
    }

    public void ResetRun()
    {
        _usedThisRun.Clear();
        _count = 0;
        _sinceLast = 99;
    }

    /// <summary>場面に合う音を確率で付ける（1 ラン 8 回まで、同じ音は 1 ランに 1 回、前の音から 6 行は空ける）。</summary>
    public void Attach(Dictionary<string, object> entry, string scene, string speaker, System.Random rng)
    {
        _sinceLast++;
        if (entry == null || !Scenes.TryGetValue(scene, out var list)) return;
        if (_count >= MaxPerRun || _sinceLast < 6) return;
        var cands = list.Where(c => c.who == speaker && _usable.ContainsKey(c.id) && !_usedThisRun.Contains(c.id)).ToList();
        if (cands.Count == 0 || rng.NextDouble() >= Chance) return;
        var pick = cands[rng.Next(cands.Count)];
        var info = _usable[pick.id];
        entry["snd"] = pick.id;
        entry["sndTiming"] = info.Timing;
        entry["sndDuration"] = Math.Round(info.Duration, 2);
        entry["sndScene"] = scene;
        _usedThisRun.Add(pick.id);
        _count++;
        _sinceLast = 0;
    }
}
#endif
