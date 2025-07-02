using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 配信設定で最大6つまで選択可能なアイテムIDリストを管理するモデル。
/// </summary>
public class StreamingSettingModel
{
    private const int MaxSelection = 6;
    private readonly Dictionary<string, int> _selected = new Dictionary<string, int>(MaxSelection);

    /// <summary>選択済みアイテムと数量の読み取り専用辞書</summary>
    public IReadOnlyDictionary<string, int> Selected => _selected;

    /// <summary>アイテムを選択に追加（初期数量=1）。成功時 true。</summary>
    public bool TryAdd(string id)
    {
        if (_selected.Count >= MaxSelection || _selected.ContainsKey(id))
            return false;
        _selected[id] = 1;
        return true;
    }

    /// <summary>数量をセット（最低1）。</summary>
    public void SetQuantity(string id, int qty)
    {
        if (_selected.ContainsKey(id))
            _selected[id] = Mathf.Max(1, qty);
    }

    /// <summary>選択から削除</summary>
    public void Remove(string id) => _selected.Remove(id);

    /// <summary>全選択クリア</summary>
    public void Clear() => _selected.Clear();
}