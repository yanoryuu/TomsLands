using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 村の遮蔽物（木・建物・小物）に「ステンシル書き込み双子」を自動生成する。
/// 双子は ColorMask 0 の不可視スプライトで、元と同じ位置・同じ描画順に
/// ステンシル値 2 を書き込むだけ。元の Lit 描画（Sprite-Lit-Default）は一切変えない。
/// プレイヤー側（PlayerOccludedSilhouette）が値 1 を書き、
/// 最終的にステンシルが 2 の画素 = 「プレイヤーの上に遮蔽物が描かれた場所」となる。
/// </summary>
public class OccluderStencilSetup : MonoBehaviour
{
    [Tooltip("この配下の全SpriteRendererに双子を生成する（Decor / Plots など）")]
    [SerializeField] private Transform[] roots;

    [Tooltip("ステンシル書き込みマテリアル（TomsLands/SpriteStencilWrite, Ref=2）")]
    [SerializeField] private Material stencilWriteMaterial;

    [Tooltip("双子を作らないオブジェクト名（部分一致）。影・発光・エフェクト類")]
    [SerializeField] private string[] excludeNameContains = { "Shadow", "Light", "Glow", "FX", "Smoke", "Fire" };

    private void Awake()
    {
        if (stencilWriteMaterial == null)
        {
            var shader = Shader.Find("TomsLands/SpriteStencilWrite");
            if (shader == null)
            {
                Debug.LogWarning("[OccluderStencilSetup] TomsLands/SpriteStencilWrite が見つかりません。遮蔽シルエットは無効です。");
                return;
            }
            stencilWriteMaterial = new Material(shader);
            stencilWriteMaterial.SetFloat("_StencilRef", 2f);
        }

        int count = 0;
        foreach (var root in roots ?? System.Array.Empty<Transform>())
        {
            if (root == null) continue;
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || IsExcluded(sr.gameObject.name)) continue;
                CreateTwin(sr);
                count++;
            }
        }
        Debug.Log($"[OccluderStencilSetup] ステンシル双子を {count} 個生成しました。");
    }

    private bool IsExcluded(string name)
    {
        foreach (var word in excludeNameContains)
        {
            if (!string.IsNullOrEmpty(word) && name.Contains(word)) return true;
        }
        return false;
    }

    private void CreateTwin(SpriteRenderer source)
    {
        var go = new GameObject("StencilTwin");
        go.transform.SetParent(source.transform, false); // 位置・アクティブ状態は親に追従

        var twin = go.AddComponent<SpriteRenderer>();
        twin.sprite = source.sprite;
        twin.flipX = source.flipX;
        twin.flipY = source.flipY;
        twin.sharedMaterial = stencilWriteMaterial;
        twin.sortingLayerID = source.sortingLayerID;
        twin.sortingOrder = source.sortingOrder;
        twin.spriteSortPoint = source.spriteSortPoint;

        // 施設の建設などで元スプライトが差し替わっても追従する
        var sync = go.AddComponent<SpriteStencilTwin>();
        sync.Initialize(source, twin);
    }
}

/// <summary>ステンシル双子のスプライト同期（元の sprite/flip が変わったときだけ反映）。</summary>
public class SpriteStencilTwin : MonoBehaviour
{
    private SpriteRenderer source;
    private SpriteRenderer twin;

    public void Initialize(SpriteRenderer source, SpriteRenderer twin)
    {
        this.source = source;
        this.twin = twin;
    }

    private void LateUpdate()
    {
        if (source == null || twin == null) return;
        if (twin.sprite != source.sprite) twin.sprite = source.sprite;
        if (twin.flipX != source.flipX) twin.flipX = source.flipX;
        if (twin.flipY != source.flipY) twin.flipY = source.flipY;
        if (twin.enabled != source.enabled) twin.enabled = source.enabled;
    }
}
