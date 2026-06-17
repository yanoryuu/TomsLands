using System;

/// <summary>
/// GameConstData を配信メタ情報で包むエンベロープ。
/// JsonUtility はトップレベルがオブジェクトかつネストした Serializable を扱えるため、
/// このラッパをそのまま FromJsonOverwrite できる。
/// </summary>
[Serializable]
public class GameConstEnvelope
{
    public int version;          // 単調増加 or コンテンツに対応する識別子
    public int schemaVersion;    // データ構造の互換性管理
    public string updatedAt;     // ISO8601 文字列（任意）
    public GameConstData data;   // 本体
}
