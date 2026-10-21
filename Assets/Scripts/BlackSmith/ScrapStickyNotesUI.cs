using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 仕入れ画面の付箋（I-6・News_Spec §9.3）と朝刊の入り口（I-7）。
/// スクラップした記事を、予想（この品が上がる）と「あと何日で効くか／結果」の2行で並べる。
/// 記事から銘柄へのリンクは作らない（付箋を押しても銘柄には飛ばない）。押すと朝刊を開く。
/// 未配線（参照が空）でも動く。
/// </summary>
public class ScrapStickyNotesUI : MonoBehaviour
{
    [SerializeField] private Button newspaperButton;
    [Tooltip("付箋を並べる親（VerticalLayoutGroup）")]
    [SerializeField] private RectTransform noteParent;
    [Tooltip("付箋1枚のひな形（Button と子の TMP）。非表示で置く")]
    [SerializeField] private Button noteTemplate;

    private readonly List<Button> notes = new();

    public event Action OnNewspaperClicked;

    private void Awake()
    {
        if (newspaperButton != null) newspaperButton.onClick.AddListener(() => OnNewspaperClicked?.Invoke());
    }

    /// <summary>付箋を出す（空なら付箋は出さず、朝刊ボタンだけ）。</summary>
    public void Show(IReadOnlyList<string> lines)
    {
        if (noteTemplate == null || noteParent == null) return;
        if (noteTemplate.gameObject.activeSelf) noteTemplate.gameObject.SetActive(false);
        int count = lines?.Count ?? 0;
        while (notes.Count < count)
        {
            var b = Instantiate(noteTemplate, noteParent);
            b.onClick.AddListener(() => OnNewspaperClicked?.Invoke());
            notes.Add(b);
        }
        for (int i = 0; i < notes.Count; i++)
        {
            var b = notes[i];
            if (b == null) continue;
            bool used = i < count;
            b.gameObject.SetActive(used);
            if (!used) continue;
            var label = b.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = lines[i];
        }
    }
}
