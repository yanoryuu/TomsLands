using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 村の格（開拓地 → 村 → 町 → 王都。VillageRank）に合わせて、マップの地面と装飾を育てる。
/// 格ごとの GameObject のまとまり（layers）を持ち、Apply(rank) で「minRank ≤ 格 ≤ maxRank」のものだけを出す。
/// 道の石畳は layers に Tilemap のまとまりを入れて切り替える（Path の上に重ねて描く）。
/// 施設が最大Lvになった区画の飾り（金の旗）は maxLevelDecor で切り替える（ApplyFacilityLevel）。
/// 中身は Assets/Prefabs/Village/VillageRankDecor.prefab（VillageRankDecorBuilder が作る）。参照は未配線（null）でも動作する。
/// </summary>
public class VillageRankDecor : MonoBehaviour
{
    [Serializable]
    public class Layer
    {
        [Tooltip("この格から出す")]
        public int minRank;
        [Tooltip("この格まで出す（上の格で片づくもの。開拓地の柵・切り株など）")]
        public int maxRank = 99;
        public GameObject root;
    }

    [Serializable]
    public class FacilityDecor
    {
        [Tooltip("VillageFacilityData.facilityId")]
        public string facilityId;
        public GameObject root;
    }

    [SerializeField] private Layer[] layers = { };

    [Tooltip("施設が最大Lvの区画に出す飾り（金の旗など）")]
    [SerializeField] private FacilityDecor[] maxLevelDecor = { };

    [Header("出てくる演出")]
    [Tooltip("新しく出た装飾が弾んで現れる時間（秒）")]
    [SerializeField] private float popDuration = 0.4f;
    [Tooltip("1つずつの時間差（秒）")]
    [SerializeField] private float popStagger = 0.025f;
    [Tooltip("石畳がふわっと敷かれる時間（秒）")]
    [SerializeField] private float tileFadeDuration = 0.6f;

    private int appliedRank = -1;

    public int AppliedRank => appliedRank;

    /// <summary>格に合わせて装飾を切り替える。animate=true なら新しく出たものだけ弾んで現れる（格が上がった瞬間用）。</summary>
    public void Apply(int rank, bool animate = false)
    {
        bool first = appliedRank < 0;
        foreach (var layer in layers ?? Array.Empty<Layer>())
        {
            if (layer == null || layer.root == null) continue;
            bool show = rank >= layer.minRank && rank <= layer.maxRank;
            bool appeared = show && !layer.root.activeSelf;
            layer.root.SetActive(show);
            if (appeared && animate && !first) PlayAppear(layer.root);
        }
        appliedRank = rank;
    }

    /// <summary>施設のレベルを伝える（最大Lv なら、その区画の飾りを出す）。</summary>
    public void ApplyFacilityLevel(string facilityId, int level, int maxLevel, bool animate = false)
    {
        if (string.IsNullOrEmpty(facilityId)) return;
        foreach (var d in maxLevelDecor ?? Array.Empty<FacilityDecor>())
        {
            if (d == null || d.root == null || d.facilityId != facilityId) continue;
            bool show = maxLevel > 0 && level >= maxLevel;
            bool appeared = show && !d.root.activeSelf;
            d.root.SetActive(show);
            if (appeared && animate) PlayAppear(d.root);
        }
    }

    private void PlayAppear(GameObject root)
    {
        int i = 0;
        foreach (Transform child in root.transform)
        {
            var tilemap = child.GetComponent<Tilemap>();
            if (tilemap != null)
            {
                FadeIn(tilemap);
                continue;
            }
            child.DOKill(true); // 途中の弾みは終わらせてから元の大きさを読む
            Vector3 baseScale = child.localScale;
            child.localScale = new Vector3(baseScale.x, baseScale.y * 0.2f, baseScale.z);
            child.DOScale(baseScale, popDuration)
                .SetDelay(i * popStagger)
                .SetEase(Ease.OutBack)
                .SetLink(child.gameObject);
            i++;
        }
        var own = root.GetComponent<Tilemap>();
        if (own != null) FadeIn(own);
    }

    private void FadeIn(Tilemap tilemap)
    {
        Color c = tilemap.color;
        c.a = 0f;
        tilemap.color = c;
        DOTween.To(() => tilemap.color.a, a =>
            {
                var col = tilemap.color;
                col.a = a;
                tilemap.color = col;
            }, 1f, tileFadeDuration)
            .SetEase(Ease.OutQuad)
            .SetLink(tilemap.gameObject);
    }
}
