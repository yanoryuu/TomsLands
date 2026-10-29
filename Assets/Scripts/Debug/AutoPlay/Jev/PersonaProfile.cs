#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 人格（ずんだもん / 四国めたん）の定義。人物像は tools/jikkyo_video/CHARACTERS.md から。
/// モデルの大きさ以外の違い（state の前置き・選び方・現金の残し方・学び方）をここにまとめる。
/// </summary>
public sealed class PersonaProfile
{
    public string Id;
    public string LabelJa;
    /// <summary>特性ベクトル（PersonaTraits）。下の定数のうち Sample・Temperature・ScoreBias・HardRouteChance・CashFloor・LearningRate・FlashyBoost はここから導く（WithTraits）。</summary>
    public PersonaTraits Traits;
    /// <summary>state の頭に付ける英文（Jev は英語の方が精度が高い）。</summary>
    public string Preamble;
    /// <summary>true = 確率分布から抽選（＋余計な揺らぎ）、false = 最尤（argmax）。</summary>
    public bool Sample;
    /// <summary>抽選の温度（1 = 分布どおり、1 より大きいほど平ら＝気まぐれ）。</summary>
    public float Temperature = 1f;
    /// <summary>予算・介入の強さの score を何段階上へずらすか（自信過剰）。</summary>
    public float ScoreBias;
    /// <summary>難しい道（情報を買わない・魔王軍に張る等）をわざと選ぶ確率。</summary>
    public float HardRouteChance;
    /// <summary>これより下には現金を減らさない（G）。0 = 制限なし。</summary>
    public int CashFloor;
    /// <summary>
    /// 学習器の λ: 最終の確率 p ∝ p_kev × exp(λ × 予測した損益の差[1 万G])（0 = 学ばない）。
    /// めたんは大きく、ずんだもんはほぼ 0（CHARACTERS.md: 「学習したのだ」と言うが学習していない）。
    /// </summary>
    public float LearningRate;
    /// <summary>学習器のリッジ（大きいほど件数が増えるまで予測を 0 に縮める＝ゆっくり学ぶ・運に振られない）。</summary>
    public float RidgePrior = 5f;
    /// <summary>学習器が古い結果を薄める割合（1 件学ぶごとに掛ける）。</summary>
    public float Forget = 0.998f;
    /// <summary>state に載せる「似た場面」の数（PersonaRecallStore.FindSimilar の k）。</summary>
    public int RecallCount = 3;
    /// <summary>state に載せる教訓の数の上限。</summary>
    public int MaxLessons;
    /// <summary>新聞を読む秒数（0 なら判断に任せる）。</summary>
    public float NewspaperSeconds;
    /// <summary>村での判断の state に足す英文（施設への投資の性格）。</summary>
    public string VillagePreamble;
    /// <summary>村資金をこれより下に減らさない（G）。0 = 制限なし。</summary>
    public int VillageFundsFloor;
    /// <summary>派手な施設（PersonaRequests.FlashyFacilities）に投資する確率の倍率（ずんだもんは目立つものに勘で突っ込む）。</summary>
    public float FlashyBoost = 1f;
    /// <summary>施設を調べて眺める秒数（村の自動操作）。</summary>
    public float VillageLookSeconds = 3f;
    /// <summary>村資金のうち残しておく割合（VillageFundsFloor と大きい方。0 = 下限だけ）。2 億G で 1 万G を残しても意味がないので割合で持つ。</summary>
    public float VillageReserveShare;

    // ---- 学習器の寄せ方（PersonaReweighter）。既定値は 2026-10-28 朝までの式 ----
    /// <summary>1 選択肢あたりの log 倍率の上限。</summary>
    public float MaxLogAdjust = PersonaReweighter.MaxLogAdjust;
    /// <summary>予測を質問の平均へ縮める件数（0 = 縮めない。未経験の選択肢は予測 0 のまま）。</summary>
    public float ShrinkCount;
    /// <summary>件数の少ない選択肢への楽観の上乗せ（log 倍率）。</summary>
    public float Optimism;

    // ---- 慎重な選び方（PersonaChooser.Careful。Sample = false のときだけ） ----
    /// <summary>
    /// true = 1 位と 2 位が近ければ場面（画面の数字）で選び、少しだけ別の手も試す。false = 最尤（argmax）・score の丸め。
    /// </summary>
    public bool Careful;
    /// <summary>選択肢の質問で「近い」とみなす確率の差（これ以上離れていれば 1 位）。</summary>
    public float SelectMargin = 0.25f;
    /// <summary>近いときの抽選の温度（1 より小さいほど 1 位寄り）。</summary>
    public float SelectTemperature = 0.5f;
    /// <summary>段階の質問で、期待値の小数部が 0.5 ± これ以内なら隣の段階と迷う。</summary>
    public float LevelBand = 0.2f;
    /// <summary>kev が 5% 以上つけた別の手（段階は隣だけ）を試す確率。</summary>
    public float ExploreRate = 0.05f;

    /// <summary>
    /// 「署名の質問」（人格そのものなので偏ってよい。エントロピーの下限・固まりの安全弁をかけない）。めたんの新聞を読む・情報を買う。
    /// </summary>
    public HashSet<string> SignatureQuestions = new();

    /// <summary>エントロピーの下限・固まりの安全弁をかける「自由な質問」か（武器の銘柄・署名の質問は除く）。</summary>
    public bool IsFreeQuestion(string q) => !string.IsNullOrEmpty(q) && q != "weapon" && !SignatureQuestions.Contains(q);

    /// <summary>村資金のうち残す額（G）。</summary>
    public int VillageReserve(int funds) =>
        Mathf.Max(VillageFundsFloor, Mathf.RoundToInt(Mathf.Max(0, funds) * Mathf.Clamp01(VillageReserveShare)));

    public static readonly PersonaProfile Zundamon = new PersonaProfile
    {
        Id = "zundamon",
        LabelJa = "ずんだもん",
        Preamble =
            "You are Zundamon, an honest and overconfident young shopkeeper. You trust your gut more than the manual, " +
            "and your reasons are often unrelated to the numbers (a lucky number, a strong-sounding item name, three viewers asking for it). " +
            "You love big bets: spending most of your cash, backing one side of the stream fully, trying the hard route without buying information. " +
            "You get sad when you lose but bounce back immediately and bet again.",
        RidgePrior = 30f,
        Forget = 0.999f,
        RecallCount = 3,
        MaxLessons = 1,
        NewspaperSeconds = 0f,
        VillagePreamble =
            "In the village you pick facilities by gut feeling: the flashy, exciting ones (the plaza with its big stream bets, the guild with rare relics, " +
            "the artisans' workshop) catch your eye first, and you rarely read the effect text to the end. You like to spend the village funds while you have them.",
        VillageFundsFloor = 0,
        VillageLookSeconds = 2.5f,
    }.WithTraits(PersonaTraits.Zundamon);

    public static readonly PersonaProfile Metan = new PersonaProfile
    {
        Id = "metan",
        LabelJa = "四国めたん",
        Preamble =
            "You are Shikoku Metan, a careful shopkeeper who is always short of money. You read the newspaper and buy information " +
            "before you act, and you reason from causes to effects. You hate spending everything: you always keep at least 10,000G in cash " +
            "and avoid bets whose downside you cannot afford. You only pay for a super chat or the demon army when the expected gain clearly beats the price.",
        // 2026-10-28: λ 1.0・リッジ 3・log ±2・未経験 = 予測 0・最尤 の組み合わせで、一度選んだ手（魔王軍・情報・出資しない・勢い 2）に
        // 固まった（配信の損益はいつもプラスなので、選んだ手の予測だけが上がり続ける）。寄せ方を弱め、未経験の手は平均扱いにし、
        // 選び方を「差が小さければ場面で決める＋たまに別の手を試す」にした（tools/persona_play/replay_selection.py で前後を比べた）
        RidgePrior = 5f,
        Forget = 0.99f,
        MaxLogAdjust = 0.7f,
        ShrinkCount = 8f,
        Optimism = 0.15f,
        Careful = true,
        RecallCount = 5,
        MaxLessons = 5,
        NewspaperSeconds = 6f,
        VillagePreamble =
            "In the village you read each facility's effect and estimate how many runs it takes to pay back its cost before investing. " +
            "You prefer facilities that make every later run safer or cheaper (the bank, the tavern's information office, the hall that unlocks higher levels). " +
            "You keep about a quarter of the village funds in reserve, but money that only sits in the village earns nothing, " +
            "so you invest the rest when a facility can plausibly pay itself back within a few runs.",
        VillageFundsFloor = 10000,
        VillageReserveShare = 0.25f,
        VillageLookSeconds = 4f,
        SignatureQuestions = new HashSet<string> { "newspaper", "intel" },
    }.WithTraits(PersonaTraits.Metan);

    /// <summary>特性を付け、特性から導く定数を入れる（P1: 2026-10-28 までの定数と同じ値になる）。</summary>
    public PersonaProfile WithTraits(PersonaTraits t)
    {
        Traits = t;
        Sample = t.Sample;
        Temperature = t.Temperature;
        ScoreBias = t.ScoreBias;
        HardRouteChance = t.HardRouteChance;
        CashFloor = t.CashFloor;
        LearningRate = t.LearningRate;
        FlashyBoost = t.FlashyBoost;
        return this;
    }

    public static readonly PersonaProfile[] All = { Zundamon, Metan };

    public static PersonaProfile Find(string id) => All.FirstOrDefault(p => p.Id == id) ?? Zundamon;
}

/// <summary>結果1件（配信1回 / ラン1回）。</summary>
public sealed class PersonaOutcome
{
    [JsonProperty("time")] public string Time;
    [JsonProperty("kind")] public string Kind;          // "stream" / "run"
    [JsonProperty("day")] public int Day;
    [JsonProperty("goldDelta")] public int GoldDelta;
    [JsonProperty("result")] public string Result;      // "win" / "lose" / "aborted" / "finished" / "bankrupt"
    [JsonProperty("choice")] public string Choice;      // 例: "side=demon,intervene=trap,budget=80%"
    [JsonProperty("lesson")] public string Lesson;
}

/// <summary>選択肢ごとの成績（事前分布の重み付け用）。</summary>
public sealed class PersonaPriorStat
{
    [JsonProperty("n")] public int Count;
    [JsonProperty("sumDelta")] public long SumDelta;
    [JsonIgnore] public float AverageDelta => Count > 0 ? (float)SumDelta / Count : 0f;
}

/// <summary>
/// 人格ごとの記憶（UserSettings/PersonaMemory/&lt;persona&gt;.json）。過去の結果と短い英語の教訓、選択肢ごとの成績。
/// </summary>
public sealed class PersonaMemory
{
    private const int MaxOutcomes = 200;

    [JsonProperty("persona")] public string Persona;
    [JsonProperty("runs")] public int Runs;
    [JsonProperty("outcomes")] public List<PersonaOutcome> Outcomes = new();
    [JsonProperty("lessons")] public List<string> Lessons = new();
    /// <summary>"side:hero" / "intervene:trap" / "buy:bargain" などのキー → 成績。</summary>
    [JsonProperty("priors")] public Dictionary<string, PersonaPriorStat> Priors = new();

    [JsonIgnore] private string _path;

    public static string PathFor(string personaId) =>
        Path.Combine(PersonaPlayConfig.ProjectRoot, "UserSettings", "PersonaMemory", personaId + ".json");

    public static PersonaMemory Load(string personaId)
    {
        string path = PathFor(personaId);
        PersonaMemory memory = null;
        try
        {
            if (File.Exists(path)) memory = JsonConvert.DeserializeObject<PersonaMemory>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 記憶ファイルを読めないので空から始めます: {path}: {e.Message}");
        }
        memory ??= new PersonaMemory();
        memory.Persona = personaId;
        memory.Outcomes ??= new List<PersonaOutcome>();
        memory.Lessons ??= new List<string>();
        memory.Priors ??= new Dictionary<string, PersonaPriorStat>();
        memory._path = path;
        return memory;
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
            Debug.LogWarning($"[PersonaPlay] 記憶ファイルを書けませんでした: {_path}: {e.Message}");
        }
    }

    /// <summary>ファイルに残す教訓の上限（state に載せるのは LatestLessons の n 件だけ）。</summary>
    private const int MaxLessonsKept = 40;

    /// <summary>
    /// 新しい順に最大 n 件の教訓。数字だけ違う同じ型の教訓は 1 件にまとめ、
    /// 「Run N: finished with …」の総括は配信の教訓より後回し（n が 3 以上のときだけ 1 件まで）。
    /// </summary>
    public List<string> LatestLessons(int n)
    {
        var list = new List<string>();
        var seen = new HashSet<string>();
        string runSummary = null;
        for (int i = Lessons.Count - 1; i >= 0 && list.Count < n; i--)
        {
            string lesson = Lessons[i];
            if (string.IsNullOrEmpty(lesson)) continue;
            if (lesson.StartsWith("Run ", StringComparison.Ordinal))
            {
                runSummary ??= lesson;
                continue;
            }
            if (!seen.Add(LessonShape(lesson))) continue;
            list.Add(lesson);
        }
        if (runSummary != null && n >= 3)
        {
            if (list.Count >= n) list[n - 1] = runSummary;
            else list.Add(runSummary);
        }
        return list;
    }

    /// <summary>数字・符号を落とした教訓の「型」（重複判定用）。ダンジョン名は残す。</summary>
    private static string LessonShape(string lesson) =>
        System.Text.RegularExpressions.Regex.Replace(lesson, @"[+\-]?\d[\d,]*", "#");

    public void Append(PersonaOutcome outcome, IEnumerable<string> priorKeys)
    {
        Outcomes.Add(outcome);
        while (Outcomes.Count > MaxOutcomes) Outcomes.RemoveAt(0);
        if (!string.IsNullOrEmpty(outcome.Lesson))
        {
            // 数字だけ違う同じ型の教訓は古い方を消して、新しい方だけ残す
            string shape = LessonShape(outcome.Lesson);
            Lessons.RemoveAll(l => LessonShape(l) == shape);
            Lessons.Add(outcome.Lesson);
        }
        while (Lessons.Count > MaxLessonsKept) Lessons.RemoveAt(0);
        if (priorKeys != null)
        {
            foreach (var key in priorKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (!Priors.TryGetValue(key, out var stat)) Priors[key] = stat = new PersonaPriorStat();
                stat.Count++;
                stat.SumDelta += outcome.GoldDelta;
            }
        }
        Save();
    }

    /// <summary>
    /// 選択肢 key の重み（1 = 中立）。平均の損益が ±30,000G で ±1 に飽和させ、exp(rate × それ) を掛ける。
    /// 試行 1 回だけでは半分しか寄せない。
    /// </summary>
    public float PriorWeight(string key, float learningRate)
    {
        if (learningRate <= 0f || !Priors.TryGetValue(key, out var stat) || stat.Count == 0) return 1f;
        float signal = Mathf.Clamp(stat.AverageDelta / 30000f, -1f, 1f);
        float trust = Mathf.Clamp01(stat.Count / 2f);
        return Mathf.Exp(learningRate * signal * trust);
    }
}
#endif
