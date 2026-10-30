using System.Collections.Generic;
using R3;
using UnityEngine;

/// <summary>
/// ターン行動ヒントのリスト表示View。
/// TomsShopメイン画面に重ねて配置し、最大3件のヒントカードを表示する。
/// </summary>
public class TurnActionHintView : MonoBehaviour
{
    [SerializeField] private Transform hintContainer;
    [SerializeField] private GameObject hintItemPrefab;

    public Subject<ActionTarget> OnHintTapped { get; } = new();

    public void Populate(List<TurnActionHint> hints)
    {
        foreach (Transform t in hintContainer)
            Destroy(t.gameObject);

        var rows = new List<RectTransform>();
        var signature = new System.Text.StringBuilder();
        foreach (var hint in hints)
        {
            if (hintItemPrefab == null) break;
            var go   = Instantiate(hintItemPrefab, hintContainer);
            var item = go.GetComponent<TurnActionHintItemUI>();
            if (item == null) continue;
            item.Setup(hint);
            item.OnTapped.Subscribe(t => OnHintTapped.OnNext(t)).AddTo(item);
            if (go.transform is RectTransform rt) rows.Add(rt);
            signature.Append(hint.Message).Append('|');
        }

        // #3 ヒントの中身が変わったときだけ札を順に出す（店に戻るたびに同じヒントが揺れないように）
        string sig = signature.ToString();
        if (sig != lastSignature && hintContainer.gameObject.activeInHierarchy) UIMotion.Stagger(rows, 0.08f);
        lastSignature = sig;
    }

    private string lastSignature;

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}
