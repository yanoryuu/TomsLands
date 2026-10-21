using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// 店ホームの新聞ボタンに付ける「今日の未読 N本」のバッジ（I-7）。0本なら隠す。増えたときだけ軽く弾ませる。
/// TomsShopPresenter が TomsShopView の子から探して数を渡す（未配置なら何もしない）。
/// </summary>
public class NewsUnreadBadge : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text countText;

    private int last = -1;

    public void SetCount(int count)
    {
        var go = root != null ? root : gameObject;
        bool show = count > 0;
        if (go.activeSelf != show) go.SetActive(show);
        if (countText != null) countText.text = count > 9 ? "9+" : count.ToString();
        if (show && count > last && last >= 0 && go.activeInHierarchy)
        {
            go.transform.DOKill(true);
            go.transform.DOPunchScale(Vector3.one * 0.25f, 0.3f, 6, 0.6f).SetLink(go);
        }
        last = count;
    }
}
