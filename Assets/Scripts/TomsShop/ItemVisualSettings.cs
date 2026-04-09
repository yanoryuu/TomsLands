using System;
using UnityEngine;

/// <summary>
/// アイテム表示に関する共通ビジュアル設定。
/// RequiredLevel に応じた背景スプライトを一元管理する。
/// プロジェクトに1つだけ作成し、ItemModel に渡して使う。
/// </summary>
[CreateAssetMenu(fileName = "ItemVisualSettings", menuName = "Settings/Item Visual Settings")]
public class ItemVisualSettings : ScriptableObject
{
    [Serializable]
    public class LevelBackground
    {
        [Tooltip("このレベル以上のアイテムに適用される背景")]
        public int minLevel = 1;
        public Sprite backgroundSprite;
    }

    [Header("レベル別背景スプライト（minLevel 降順で評価）")]
    [Tooltip("minLevel が大きい順にマッチ判定します。最初にヒットしたものを使用します。")]
    [SerializeField] private LevelBackground[] levelBackgrounds;

    /// <summary>
    /// 指定レベルに対応する背景スプライトを返す。
    /// levelBackgrounds を minLevel 降順で走査し、最初に level >= entry.minLevel となるものを返す。
    /// 該当なしなら null。
    /// </summary>
    public Sprite GetBackground(int level)
    {
        if (levelBackgrounds == null || levelBackgrounds.Length == 0) return null;

        // 降順で走査（Inspector で降順に並べることを推奨。念のためソートしても良いが毎フレーム呼ばないので問題ない）
        Sprite result = null;
        int bestMin = -1;
        foreach (var entry in levelBackgrounds)
        {
            if (level >= entry.minLevel && entry.minLevel > bestMin)
            {
                bestMin = entry.minLevel;
                result = entry.backgroundSprite;
            }
        }
        return result;
    }
}

