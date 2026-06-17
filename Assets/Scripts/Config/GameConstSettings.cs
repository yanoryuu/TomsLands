using UnityEngine;

/// <summary>
/// <see cref="GameConstData"/> を Inspector で編集するための ScriptableObject。
/// これが「ベイク済みデフォルト値」になり、実行時に <see cref="GameConst"/> が読み込む。
/// 将来サーバー配信を導入したら、ダウンロードした JSON で GameConst.Override して上書きする。
/// </summary>
[CreateAssetMenu(fileName = "GameConstSettings", menuName = "TomsLands/GameConst Settings")]
public class GameConstSettings : ScriptableObject
{
    public GameConstData data = new GameConstData();
}
