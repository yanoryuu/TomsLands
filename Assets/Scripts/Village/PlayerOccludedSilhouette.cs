using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// プレイヤー（トコ）が木や建物の後ろに隠れたとき、隠れた部分だけ白いシルエットを描く。
/// 仕組み（ステンシル方式・OccluderStencilSetup とペア）:
///  1. プレイヤーの各パーツの複製（マーク）が自分の領域にステンシル 1 を書く
///     （SortingGroup 内の最背面 order で描くため見た目には影響しない）
///  2. 遮蔽物の双子がステンシル 2 を書く（Y ソートでプレイヤーより手前のときだけ後から書かれる）
///  3. 最前面の別 SortingGroup で白シルエット複製を描き、ステンシル == 2 の画素だけ通す
/// プレイヤーはアニメーションで sprite が毎フレーム変わるため、複製は LateUpdate で追従する。
/// </summary>
public class PlayerOccludedSilhouette : MonoBehaviour
{
    [Tooltip("ステンシル1書き込みマテリアル（TomsLands/SpriteStencilWrite, Ref=1）")]
    [SerializeField] private Material stencilMarkMaterial;
    [Tooltip("白シルエットマテリアル（TomsLands/SpriteOccludedSilhouette）")]
    [SerializeField] private Material silhouetteMaterial;
    [Tooltip("シルエットの描画順（Objectsレイヤー内の最前面にする）")]
    [SerializeField] private int silhouetteSortingOrder = 5000;

    private struct Pair
    {
        public SpriteRenderer Source;
        public SpriteRenderer Mark;       // 自分の領域にステンシル1を書く複製（本体グループ内）
        public SpriteRenderer Silhouette; // 遮蔽部分だけ白く出る複製（最前面グループ）
    }

    private readonly List<Pair> pairs = new();
    private Transform silhouetteRoot;

    private void Start()
    {
        if (stencilMarkMaterial == null)
        {
            var shader = Shader.Find("TomsLands/SpriteStencilWrite");
            if (shader != null)
            {
                stencilMarkMaterial = new Material(shader);
                stencilMarkMaterial.SetFloat("_StencilRef", 1f);
            }
        }
        if (silhouetteMaterial == null)
        {
            var shader = Shader.Find("TomsLands/SpriteOccludedSilhouette");
            if (shader != null) silhouetteMaterial = new Material(shader);
        }
        if (stencilMarkMaterial == null || silhouetteMaterial == null)
        {
            Debug.LogWarning("[PlayerOccludedSilhouette] シェーダーが見つからないため遮蔽シルエットは無効です。");
            enabled = false;
            return;
        }

        var group = GetComponent<SortingGroup>();
        string sortingLayer = group != null ? group.sortingLayerName : "Objects";

        // 最前面シルエット用のルート（トップレベル + 独自 SortingGroup で
        // プレイヤーの SortingGroup の外に出し、常に遮蔽物より後に描かせる）
        var rootGo = new GameObject("PlayerSilhouette");
        silhouetteRoot = rootGo.transform;
        var silGroup = rootGo.AddComponent<SortingGroup>();
        silGroup.sortingLayerName = sortingLayer;
        silGroup.sortingOrder = silhouetteSortingOrder;

        foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr == null || sr.GetComponent<SpriteStencilTwin>() != null) continue;

            // ① 本体グループ内・最背面のステンシルマーク（見た目に出ない）
            var markGo = new GameObject("StencilMark");
            markGo.transform.SetParent(sr.transform, false);
            var mark = markGo.AddComponent<SpriteRenderer>();
            mark.sharedMaterial = stencilMarkMaterial;
            mark.sortingLayerID = sr.sortingLayerID;
            mark.sortingOrder = -1000; // グループ内の最初に描く

            // ② 最前面の白シルエット複製（ワールド位置を毎フレーム追従）
            var silGo = new GameObject($"Sil_{sr.gameObject.name}");
            silGo.transform.SetParent(silhouetteRoot, false);
            var sil = silGo.AddComponent<SpriteRenderer>();
            sil.sharedMaterial = silhouetteMaterial;
            sil.sortingLayerName = sortingLayer;
            sil.sortingOrder = sr.sortingOrder; // パーツ間の重なりは元と同じに

            pairs.Add(new Pair { Source = sr, Mark = mark, Silhouette = sil });
        }

        Debug.Log($"[PlayerOccludedSilhouette] {pairs.Count} パーツのシルエットを生成しました。");
    }

    private void LateUpdate()
    {
        if (silhouetteRoot == null) return;

        foreach (var p in pairs)
        {
            if (p.Source == null) continue;
            bool visible = p.Source.enabled && p.Source.gameObject.activeInHierarchy && p.Source.sprite != null;

            SyncSprite(p.Source, p.Mark, visible);
            SyncSprite(p.Source, p.Silhouette, visible);

            // シルエットは別ルート配下なのでワールド変換を毎フレームコピーする
            if (visible)
            {
                var t = p.Source.transform;
                p.Silhouette.transform.SetPositionAndRotation(t.position, t.rotation);
                p.Silhouette.transform.localScale = t.lossyScale;
            }
        }
    }

    private static void SyncSprite(SpriteRenderer source, SpriteRenderer target, bool visible)
    {
        if (target == null) return;
        if (target.enabled != visible) target.enabled = visible;
        if (!visible) return;
        if (target.sprite != source.sprite) target.sprite = source.sprite;
        if (target.flipX != source.flipX) target.flipX = source.flipX;
        if (target.flipY != source.flipY) target.flipY = source.flipY;
    }

    private void OnDestroy()
    {
        if (silhouetteRoot != null) Destroy(silhouetteRoot.gameObject);
    }
}
