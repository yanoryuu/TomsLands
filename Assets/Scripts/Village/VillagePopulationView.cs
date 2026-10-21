using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 村の発展（総合Lv）に応じて町人を増やす。
/// 町人は並び順どおりに「引っ越してくる」: 総合Lv が levelsPerTownsfolk 上がるごとに次の1人が現れる。
/// 見た目・家（配置）は SpumMobGenerator が並び順ごとに固定で焼き込むので、毎回同じ人が同じ場所に住む。
/// 名前付きの村人（旅人・大工など）はここでは扱わない（常にいる）。
/// </summary>
public class VillagePopulationView : MonoBehaviour
{
    [Tooltip("引っ越してくる順に並べた町人（SpumMobGenerator が設定）。シーン上では非表示で置いておく")]
    [SerializeField] private VillagerCharacter[] townsfolk = { };

    [Tooltip("総合Lv がこの数だけ上がるごとに町人が1人増える（1 なら Lv1 で1人目、Lv2 で2人目…）")]
    [Min(1)] [SerializeField] private int levelsPerTownsfolk = 1;

    [Header("引っ越し演出")]
    [Tooltip("現れるときのポップ時間（秒）")]
    [SerializeField] private float appearDuration = 0.45f;
    [Tooltip("同時に複数人増えたときの時間差（秒）")]
    [SerializeField] private float appearStagger = 0.12f;

    private readonly Dictionary<VillagerCharacter, Vector3> baseScales = new();
    private int shownCount = -1;

    public int SlotCount => townsfolk != null ? townsfolk.Length : 0;

    /// <summary>総合Lv に対する町人の人数（枠数で頭打ち）。</summary>
    public int CountFor(int villageLevel) => Mathf.Clamp(villageLevel / Mathf.Max(1, levelsPerTownsfolk), 0, SlotCount);

    private void Awake()
    {
        foreach (var v in townsfolk)
        {
            if (v == null) continue;
            baseScales[v] = v.transform.localScale;
        }
        // Presenter が Apply するまでは誰もいない（一瞬だけ全員見える、を防ぐ）
        if (shownCount < 0) SetVisibleCount(0, false);
    }

    /// <summary>
    /// 総合Lv に合わせて町人を表示する。animate=true なら新しく増えた人だけポップして現れる。
    /// </summary>
    public void Apply(int villageLevel, bool animate)
    {
        SetVisibleCount(CountFor(villageLevel), animate);
    }

    /// <summary>
    /// 総合Lv を最大Lv に対する割合で町人の枠に割り当てて表示する（施設が全部建ちきったら全員そろう）。
    /// maxVillageLevel が0以下なら Apply と同じ。
    /// </summary>
    public void ApplyScaled(int villageLevel, int maxVillageLevel, bool animate)
    {
        if (maxVillageLevel <= 0) { Apply(villageLevel, animate); return; }
        int count = Mathf.CeilToInt(Mathf.Clamp01(villageLevel / (float)maxVillageLevel) * SlotCount);
        SetVisibleCount(count, animate);
    }

    private void SetVisibleCount(int count, bool animate)
    {
        int appeared = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            var v = townsfolk[i];
            if (v == null) continue;
            bool show = i < count;
            bool wasShown = v.gameObject.activeSelf;
            v.gameObject.SetActive(show);
            if (!show || wasShown) continue;

            if (animate) PlayAppear(v, appeared++ * appearStagger);
            else ResetScale(v);
        }
        shownCount = count;
    }

    private void PlayAppear(VillagerCharacter v, float delay)
    {
        var t = v.transform;
        Vector3 scale = BaseScale(v);
        t.DOKill();
        t.localScale = Vector3.zero;
        t.DOScale(scale, appearDuration).SetDelay(delay).SetEase(Ease.OutBack).SetLink(v.gameObject);
    }

    private void ResetScale(VillagerCharacter v)
    {
        v.transform.DOKill();
        v.transform.localScale = BaseScale(v);
    }

    private Vector3 BaseScale(VillagerCharacter v) => baseScales.TryGetValue(v, out var s) ? s : Vector3.one;
}
