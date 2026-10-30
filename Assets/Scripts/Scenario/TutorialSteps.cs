/// <summary>
/// 会話チュートリアルの進行段階をまとめた判定ヘルパー（状態は持たない）。
/// 既読フラグ・段階フラグはどちらも MetaProgressModel の tutorialFlags に入れる
/// （会話ラベル以外のIDも置ける。ラン削除の対象外なので、ランをまたいでも一度きり）。
/// タイトル・店・鍛冶屋の各 Presenter が同じ条件で判定できるよう、ここに一元化する。
/// </summary>
public static class TutorialSteps
{
    // ---- 会話ラベル（Assets/TomsScenario/Scenarios/Tutorial.csv のラベルと一致させること） ----
    public const string Opening           = "Tutorial_Opening";
    public const string ShopBasics        = "Tutorial_ShopBasics";
    public const string Blacksmith        = "Tutorial_Blacksmith";
    public const string AfterProcurement  = "Tutorial_AfterProcurement";
    public const string Display           = "Tutorial_Display";
    /// <summary>1日目の営業後（2日目の朝）、店で「お金が増えてない」と気づく会話。</summary>
    public const string FundsShortage     = "Tutorial_FundsShortage";
    /// <summary>鍛冶屋で入金ルールの説明を受け、資金を立て替えてもらうまでの会話。</summary>
    public const string FundsAdvance      = "Tutorial_FundsAdvance";
    /// <summary>立て替え後、「需要が高く価格が安い」商品の選び方を教わる会話。</summary>
    public const string FundsAdvanceAfter = "Tutorial_FundsAdvanceAfter";
    public const string ProphetMeeting    = "Tutorial_ProphetMeeting";
    /// <summary>プロローグの後半（暗幕を開けてから、棚の商品が時代遅れだと気づく）。</summary>
    public const string OpeningShop       = "Tutorial_OpeningShop";
    /// <summary>初配信の前日、勇者に何を持たせるかを促す会話。</summary>
    public const string HeroEquip         = "Tutorial_HeroEquip";

    // ---- 画面を初めて開いたときの会話（Assets/TomsScenario/Scenarios/CharacterIntro.csv のラベルと一致させること） ----
    public const string IntroDemonLord    = "Intro_DemonLord";
    /// <summary>
    /// 魔王軍支援の配当のオッズ（配信開始時の見込みの段で倍率が変わる）。オッズが全部 1 のあいだは流さない。
    /// Intro_DemonLord とは別のラベルにして、オッズより前に魔王城を訪れたセーブにも一度流す。
    /// </summary>
    public const string IntroSupportOdds  = "Intro_SupportOdds";
    /// <summary>配当の元本保証（勇者が負ければ配当は出資の合計を下回らない）。streamTuning.supportPayoutFloorToStake のときだけ。</summary>
    public const string IntroSupportFloor = "Intro_SupportFloor";
    public const string IntroProphet      = "Intro_Prophet";
    /// <summary>
    /// 占い師の「上げる枠・下げる枠」の使い方（力・代金・効く日数・出し入れ不可・仕入れにも効く）。
    /// 廃止した「3 日先を占う」の頃に Intro_Prophet を見たセーブにも流すため、別のラベルにしている。
    /// </summary>
    public const string IntroProphetRules = "Intro_ProphetRules";

    /// <summary>
    /// 画面に入ったときに流す会話を、流す順に返す（既読かどうかは見ない）。
    /// 説明の会話をあとから足したとき、前の会話を見たセーブにも足した分だけが流れるよう、ラベルを分けて並べる。
    /// </summary>
    public static string[] IntroLabelsOnEnter(TomsShopGamePhase phase, StreamTuningData tuning = null)
    {
        switch (phase)
        {
            case TomsShopGamePhase.Prophet:
                return new[] { IntroProphet, IntroProphetRules };
            case TomsShopGamePhase.DungeonLevelUp:
                var labels = new System.Collections.Generic.List<string> { IntroDemonLord };
                if (SupportOdds.IsActive(tuning)) labels.Add(IntroSupportOdds);
                if (SupportOdds.FloorsToStake(tuning)) labels.Add(IntroSupportFloor);
                return labels.ToArray();
            default:
                return System.Array.Empty<string>();
        }
    }

    // ---- 会話ではない段階フラグ ----
    /// <summary>初回のオート購入（所持金全額・調整不可）を済ませた。</summary>
    public const string StepFirstAutoBuy      = "TutorialStep_FirstAutoBuy";
    /// <summary>鍛冶屋の立て替え資金を受け取った（二重付与防止）。</summary>
    public const string StepFundsAdvanceGranted = "TutorialStep_FundsAdvanceGranted";

    /// <summary>鍛冶屋が立て替えてくれる資金(G)。返済は不要（台本に返済の話が無いため）。</summary>
    public const int FundsAdvanceAmount = 50000;

    /// <summary>
    /// 「初めから」で村・準備シーンを経由せず、店（TomsShop）で直接開店させるか。
    /// 冒頭の会話を見ていないスロット＝初回プレイとみなす。
    /// </summary>
    public static bool ShouldStartDirectlyInShop(MetaProgressModel meta) =>
        meta != null && !meta.HasSeenTutorial(Opening);

    /// <summary>
    /// 初回のオート購入待ちか。この間、店では鍛冶屋以外を押せず、
    /// 鍛冶屋ではオート購入（全額・調整不可）の「購入」しか押せない。
    /// AfterProcurement を見ている＝旧版で仕入れチュートリアルを終えたセーブなので対象外。
    /// </summary>
    public static bool IsFirstAutoBuyPending(MetaProgressModel meta) =>
        meta != null
        && !meta.HasSeenTutorial(StepFirstAutoBuy)
        && !meta.HasSeenTutorial(AfterProcurement);
}
