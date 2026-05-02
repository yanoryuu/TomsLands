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

        foreach (var hint in hints)
        {
            if (hintItemPrefab == null) break;
            var go   = Instantiate(hintItemPrefab, hintContainer);
            var item = go.GetComponent<TurnActionHintItemUI>();
            if (item == null) continue;
            item.Setup(hint);
            item.OnTapped.Subscribe(t => OnHintTapped.OnNext(t));
        }
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}
