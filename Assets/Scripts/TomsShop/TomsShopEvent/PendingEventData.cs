using R3;

/// <summary>
/// TomsShop内でインライン表示する保留イベントデータ。
/// イベントは会話（宴の Event_&lt;id&gt;）で流し、もらったもの等は会話の最後の行（Event.csv の &lt;param=EventResult&gt;）に出し、終わったら効果を入れる。会話が無いときだけポップアップで出す。
/// シーン遷移を伴わないため、通常クラスとしてSingleton登録して使用する。
/// 将来EventSceneへ切り替える場合は、UseInlineEventPopup=falseにして
/// GameFlowManager側でGoToEvent()を使うようにする。
/// </summary>
public class PendingEventData
{
    /// <summary>
    /// true の場合、TomsShop内のポップアップでイベントを表示する。
    /// false の場合、EventSceneへシーン遷移する（将来用）。
    /// </summary>
    public bool UseInlineEventPopup { get; set; } = true;

    /// <summary>保留中のイベントデータ</summary>
    public TomsEvent PendingEvent { get; private set; }

    /// <summary>保留イベントがあるかどうか</summary>
    public bool HasPendingEvent => PendingEvent != null;

    /// <summary>ランダムイベントの会話ラベルの接頭辞。イベントIDを付けて Event_001 のようになる。</summary>
    public const string ConversationLabelPrefix = "Event_";

    /// <summary>イベントの会話ラベル（Event.csv の *Event_&lt;id&gt;）。</summary>
    public static string ConversationLabel(string eventId) => ConversationLabelPrefix + eventId;

    /// <summary>
    /// イベントを会話で流している最中か。会話が終わると効果が入り、保留が消える。
    /// 店の画面の入り直しで二重に流さないため・自動プレイがポップアップの確認ボタンを押しに行かないために使う。
    /// </summary>
    public bool IsPlayingConversation { get; set; }

    /// <summary>保留イベントのGameFlowインデックス</summary>
    public int GameFlowIndex { get; private set; }

    /// <summary>
    /// イベントを保留状態にセットする
    /// </summary>
    public void Set(TomsEvent tomsEvent, int gameFlowIndex)
    {
        PendingEvent = tomsEvent;
        GameFlowIndex = gameFlowIndex;
    }

    /// <summary>
    /// 保留状態をクリアする
    /// </summary>
    public void Clear()
    {
        bool had = PendingEvent != null;
        PendingEvent = null;
        GameFlowIndex = 0;
        if (had) cleared.OnNext(Unit.Default);
    }

    private readonly Subject<Unit> cleared = new();

    /// <summary>
    /// 保留イベントが消えたとき（確認ボタン・表示失敗）の通知。
    /// GameLifecycleHandler が受けて、保存してある表示待ちイベント（tomsData）も消す。
    /// </summary>
    public Observable<Unit> OnCleared => cleared;
}

