using System.Linq;
using UnityEngine;

/// <summary>
/// 介入まわりのシーン参照（UI 実装は別担当）。どれも null 可で、欠けた部分は何もしない。
/// BattleLifetimeScope が Inspector 値 → シーン検索の順で解決して登録する。
/// </summary>
public sealed class InterventionSceneRefs
{
    public IInterventionCardView CardView { get; }
    public ISpecialMoveCutIn CutIn { get; }
    public StreamingCommentDirector CommentDirector { get; }

    public InterventionSceneRefs(IInterventionCardView cardView, ISpecialMoveCutIn cutIn, StreamingCommentDirector commentDirector)
    {
        CardView = cardView;
        CutIn = cutIn;
        CommentDirector = commentDirector;
    }

    /// <summary>
    /// Inspector で割り当てた MonoBehaviour（インターフェース実装）を優先し、無ければシーンから探す。
    /// </summary>
    public static InterventionSceneRefs Resolve(MonoBehaviour cardView, MonoBehaviour cutIn, StreamingCommentDirector director)
    {
        var card = cardView as IInterventionCardView;
        var cut = cutIn as ISpecialMoveCutIn;
        if (cardView != null && card == null)
            Debug.LogWarning($"[InterventionSceneRefs] {cardView.name} は IInterventionCardView を実装していません。");
        if (cutIn != null && cut == null)
            Debug.LogWarning($"[InterventionSceneRefs] {cutIn.name} は ISpecialMoveCutIn を実装していません。");

        if (card == null || cut == null)
        {
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            card ??= all.OfType<IInterventionCardView>().FirstOrDefault();
            cut ??= all.OfType<ISpecialMoveCutIn>().FirstOrDefault();
        }
        if (director == null)
            director = Object.FindFirstObjectByType<StreamingCommentDirector>(FindObjectsInactive.Include);

        if (card == null) Debug.Log("[InterventionSceneRefs] 介入カード（IInterventionCardView）が見つかりません。介入ボタンは出ません（ロジックは動作）。");
        if (cut == null) Debug.Log("[InterventionSceneRefs] 必殺技カットイン（ISpecialMoveCutIn）が見つかりません。必殺技は演出なしで出ます。");
        return new InterventionSceneRefs(card, cut, director);
    }
}
