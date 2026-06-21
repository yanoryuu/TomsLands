using UnityEditor;
using UnityEngine;

/// <summary>
/// GameConstSettings(SO) のInspector。
/// このアセットは「ベイク済みデフォルト」（オフライン/取得失敗時のフォールバックとして使われる）なので
/// 編集欄はそのまま残しつつ、実際の配信値はリモート確認ウィンドウで見るよう導線を足す。
/// （ItemData と違い、こちらの値はフォールバックとして実際に使われるため非表示にはしない）
/// </summary>
[CustomEditor(typeof(GameConstSettings))]
public class GameConstSettingsInspector : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "これは「ベイク済みデフォルト」です（サーバー取得失敗・オフライン時のフォールバックに使用）。\n" +
            "実際にゲームへ適用される配信値は [Tools > GameConst > リモート確認ウィンドウ] で確認してください。",
            MessageType.Info);

        if (GUILayout.Button("リモート確認ウィンドウを開く"))
            GameConstWindow.Open();

        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}
