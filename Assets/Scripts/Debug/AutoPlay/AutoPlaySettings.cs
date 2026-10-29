#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

/// <summary>会話（Utage）の扱い。</summary>
public enum AutoPlayScenarioMode
{
    /// <summary>一定間隔で1メッセージずつ送る（録画で読める速さ）。</summary>
    Read = 0,
    /// <summary>毎フレーム送って最速で飛ばす。</summary>
    Fast = 1,
    /// <summary>送らない（会話中は待つだけ。手で送る）。</summary>
    Off = 2,
}

/// <summary>タイトル画面での行動。</summary>
public enum AutoPlayTitleAction
{
    /// <summary>セーブがあれば「続きから」、無ければ空きスロットで「初めから」。</summary>
    ContinueOrNewGame = 0,
    /// <summary>「続きから」だけ（セーブが無ければ停止）。</summary>
    ContinueOnly = 1,
    /// <summary>空きスロットで「初めから」（空きが無ければ停止。上書きはしない）。</summary>
    NewGameInEmptySlot = 2,
    /// <summary>タイトルに来たら停止する。</summary>
    Stop = 3,
}

/// <summary>ラン終了（リザルト / ゲームオーバー）での行動。</summary>
public enum AutoPlayRunEndAction
{
    /// <summary>リザルト演出・会話を最後まで流してから停止する。</summary>
    Stop = 0,
    /// <summary>村 → 出店準備 → 次のランへ進み続ける。</summary>
    StartNextRun = 1,
    /// <summary>リザルト / ゲームオーバーのボタンを押して精算し（村資金・ランの記録を保存）、村に着いたところで停止する。
    /// 人格マラソンが同じセーブを次の Play で「続きから」遊ぶため（Stop のままだと精算されずにラン内セーブが残る）。</summary>
    SettleAndStopAtVillage = 2,
}

/// <summary>
/// オートプレイの設定。仕様: Docs/AutoPlay_Usage.md
/// AutoPlay.Start(settings) に渡すか、AutoPlay.Settings を直接書き換える（実行中の変更も次の行動から反映）。
/// </summary>
[Serializable]
public sealed class AutoPlaySettings
{
    /// <summary>速度倍率（Time.timeScale）。1 / 2 / 4 を想定。停止時に開始前の値へ戻す。</summary>
    public float TimeScale = 1f;

    /// <summary>行動と行動の間のウェイト（秒）。録画で見やすいよう 0.5〜1 秒程度。</summary>
    public float ActionWait = 0.8f;

    /// <summary>true ならウェイトを timeScale に連動させる（x2 なら実時間は半分）。</summary>
    public bool WaitFollowsTimeScale = true;

    /// <summary>ボタンを押したときの「押し込み」表示時間（実時間・秒）。</summary>
    public float ButtonHoldSeconds = 0.12f;

    /// <summary>会話の送り方。</summary>
    public AutoPlayScenarioMode ScenarioMode = AutoPlayScenarioMode.Read;

    /// <summary>Read モードでの1メッセージあたりの間隔（秒。WaitFollowsTimeScale に従う）。</summary>
    public float ScenarioReadInterval = 1.2f;

    /// <summary>進めるターン数（0 = 無制限）。開始時のターンから数える。</summary>
    public int TurnLimit = 0;

    /// <summary>タイトル画面での行動。</summary>
    public AutoPlayTitleAction TitleAction = AutoPlayTitleAction.ContinueOrNewGame;

    /// <summary>優先して使うセーブスロット（0 始まり。-1 = 自動）。</summary>
    public int PreferredSlot = -1;

    /// <summary>ラン終了時の行動。</summary>
    public AutoPlayRunEndAction RunEndAction = AutoPlayRunEndAction.Stop;

    /// <summary>シーンに入ってから最初の行動までの待ち（実時間・秒）。演出を見せるため。</summary>
    public float SceneSettleSeconds = 1.5f;

    /// <summary>フェーズ・状態がこの秒数（実時間）変わらなければ「詰まり」とみなす。</summary>
    public float StallSeconds = 40f;

    /// <summary>詰まり復帰（汎用の閉じる/次へ）を連続で試す上限。超えたら停止する。</summary>
    public int MaxStallRecoveries = 5;

    /// <summary>店/配信エンジン（DebugAutoPlayer）が一時停止したとき、自動復帰を試みるまでの秒数（実時間）。</summary>
    public float PauseRecoverySeconds = 4f;

    /// <summary>LogError / 例外を検出したらエンジンを一時停止する（自動復帰の対象）。</summary>
    public bool PauseOnErrorLog = false;

    /// <summary>自動で選べない確認ダイアログに「キャンセル（無ければ確認）」で答える。</summary>
    public bool AutoAnswerUnknownPopups = true;

    // ---- 配信中（FightScene）の操作 ----

    /// <summary>配信中に介入・補充・棚の入れ替え・値動き確認を自動で行う。false なら自動進行を待つだけ。</summary>
    public bool StreamActionsEnabled = true;

    /// <summary>配信中の操作の間隔（秒。WaitFollowsTimeScale に従う。ActionWait より短くはならない）。</summary>
    public float StreamActionInterval = 3f;

    /// <summary>1配信で介入に使ってよい額（自動操作を始めた時点の残高に対する割合 0〜1）。</summary>
    public float StreamSpendRatio = 0.5f;

    /// <summary>値動きグラフを開いてから閉じるまで（実時間・秒）。</summary>
    public float PriceGraphPeekSeconds = 1.5f;

    // ---- 配信前の仕入れ ----

    /// <summary>次の配信まで何営業日以内なら「配信前の仕入れ」（ダンジョン特化を混ぜる）にするか。1 = 配信の前日と配信日の朝。</summary>
    public int StreamPrepTurns = 1;

    /// <summary>次の配信まで何営業日以内なら、弱点が分からないとき情報屋で情報を買うか（戦略が判断）。</summary>
    public int IntelPrepTurns = 2;

    /// <summary>戦略の乱数シード（0 = 時刻から）。</summary>
    public int RandomSeed = 0;

    public AutoPlaySettings Clone() => (AutoPlaySettings)MemberwiseClone();
}
#endif
