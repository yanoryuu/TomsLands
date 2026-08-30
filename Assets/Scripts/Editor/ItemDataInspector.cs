using UnityEditor;
using UnityEngine;

/// <summary>
/// ItemData(SO) のInspectorをカスタム表示にする。
/// 数値・種類・属性・名前・説明などはスプレッドシート/サーバーで管理され、
/// SO側の値はゲームに反映されない（誤解防止のため編集欄を隠す）。
/// SOで意味を持つのは itemId（上書きの突合キー）と itemIcon（ビジュアル）のみ。
/// </summary>
[CustomEditor(typeof(ItemData))]
public class ItemDataInspector : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "価格・在庫・種類・属性・名前・説明などは スプレッドシート/サーバー で管理されます。\n" +
            "ここでの編集はゲームに反映されません。\n" +
            "配信中の値は [Tools > TomsLands > リモート設定 > Item一覧ウィンドウ] で確認してください。",
            MessageType.Info);

        EditorGUILayout.Space();

        serializedObject.Update();

        // SOで意味を持つフィールドのみ編集可能に表示
        var idProp = serializedObject.FindProperty("itemId");
        if (idProp != null)
            EditorGUILayout.PropertyField(idProp, new GUIContent("Item Id", "上書きの突合キー（シートのitemIdと一致させる）"));

        var iconProp = serializedObject.FindProperty("itemIcon");
        if (iconProp != null)
            EditorGUILayout.PropertyField(iconProp, new GUIContent("Item Icon", "アイコン（SOで管理する唯一のビジュアル）"));

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        if (GUILayout.Button("Item一覧ウィンドウを開く"))
            ItemMasterWindow.Open();
    }
}
