using R3;

/// <summary>占い師の枠の向き（上げる枠＝緑・下げる枠＝赤）。int 値は保存・ログにそのまま使ってよい。</summary>
public enum ProphetDirection
{
    /// <summary>▲ 上げる枠（需要の均衡点を上へ）。</summary>
    Up = 1,
    /// <summary>▼ 下げる枠（需要の均衡点を下へ）。</summary>
    Down = -1,
}

/// <summary>
/// 枠に品のカードを入れられるか（入れたか）の答え。画面は Ok 以外のときに占い師のセリフで理由を言う
/// （ProphetPresenter.ReasonLine。セリフは Resources_moved/ProphetDialogues.csv の place_* 行）。
/// </summary>
public enum ProphetPlaceResult
{
    /// <summary>入れられる（TryPlace なら入れた）。</summary>
    Ok = 0,
    /// <summary>占い師の力が 0（配信を終えると 1 つ戻る）。</summary>
    NoPower = 1,
    /// <summary>代金が足りない（Cost &gt; 所持金）。</summary>
    NotEnoughMoney = 2,
    /// <summary>その枠はもう使っている（効果が切れるまで出し入れ不可）。</summary>
    SlotInUse = 3,
    /// <summary>もう片方の枠に同じ品が入っている（同じ品を両方の枠には入れられない）。</summary>
    SameItemInOtherSlot = 4,
    /// <summary>まだ解放されていない品（鍛冶屋のレベルが足りない）。</summary>
    NotUnlocked = 5,
    /// <summary>知らない品 id・空の id。</summary>
    UnknownItem = 6,
}

/// <summary>枠の中身（空なら GetSlot が null を返す）。</summary>
public readonly struct ProphetSlotState
{
    public readonly string ItemId;
    public readonly ProphetDirection Direction;
    /// <summary>あと何日効くか（1 以上。0 になった朝に枠から外れる）。</summary>
    public readonly int TurnsLeft;
    /// <summary>入れたときの効く日数（目盛りの数。仮 3）。</summary>
    public readonly int TotalTurns;

    public ProphetSlotState(string itemId, ProphetDirection direction, int turnsLeft, int totalTurns)
    {
        ItemId = itemId;
        Direction = direction;
        TurnsLeft = turnsLeft;
        TotalTurns = totalTurns;
    }
}

/// <summary>
/// 占い師の「上げる枠・下げる枠」の仕組みと画面の間の口（Docs/Prophet_Intervention.md）。画面（ProphetPresenter）はこれだけを見る。
/// 本物の実装は仕組み側（wt-04）が作り、GameLifetimeScope の登録 1 行を差し替える。それまでは ProphetInterventionStub（メモリ上だけ）。
///
/// 決まり（仕組み側が守る）:
///   ・上げる枠・下げる枠が 1 つずつ。同時に効くのは上げる 1・下げる 1。同じ品を両方の枠には入れられない
///   ・入れた日から <see cref="EffectTurns"/> 日（仮 3）効く。1 日＝朝の更新ごとに TurnsLeft が 1 減り、0 で枠から外れる。入れたら切れるまで出し入れ不可
///   ・効果は需要の均衡点をずらす。切れたら元の需要へ戻ろうと動く。値段は 1 つなので仕入れ・売りの両方に効く
///   ・入れるたびに占い師の力を 1 つ使う。力は最大 <see cref="MaxPower"/>（2）。配信を終えると（途中終了でも）+1。ラン開始時は 2
///   ・1 回の代金 <see cref="Cost"/>（仮 15,000G。取らないなら 0）。お金と力は TryPlace の中で引く（画面は引かない）
/// </summary>
public interface IProphetIntervention
{
    /// <summary>今の占いの力（0〜MaxPower）。</summary>
    int Power { get; }

    /// <summary>力の最大（2）。</summary>
    int MaxPower { get; }

    /// <summary>1 回の代金（取らないなら 0）。画面は「力を 1 つ使う・15,000G」の表示に使う。</summary>
    int Cost { get; }

    /// <summary>入れたときに効く日数（仮 3）。画面の「需要 ▲ 3 ターン」の表示と目盛りに使う。</summary>
    int EffectTurns { get; }

    /// <summary>枠の中身。空なら null。</summary>
    ProphetSlotState? GetSlot(ProphetDirection direction);

    /// <summary>状態（力・枠の中身・残り日数）が変わった。画面はこれを購読して描き直す。</summary>
    Observable<Unit> Changed { get; }

    /// <summary>その品をその枠に入れられるか（何も変えない）。画面は押せない見た目と理由のセリフに使う。</summary>
    ProphetPlaceResult CanPlace(string itemId, ProphetDirection direction);

    /// <summary>入れる。Ok なら力を 1 つ・代金を引き、状態を保存して Changed を流す。Ok 以外なら何も変えない。</summary>
    ProphetPlaceResult TryPlace(string itemId, ProphetDirection direction);
}
