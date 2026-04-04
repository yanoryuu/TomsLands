/// <summary>
/// TomsShop内でインライン表示する保留イベントデータ。
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
        PendingEvent = null;
        GameFlowIndex = 0;
    }
}

