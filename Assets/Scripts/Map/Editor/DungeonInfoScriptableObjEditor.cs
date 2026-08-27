#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DungeonInfoScriptableObj))]
public class DungeonInfoScriptableObjEditor : Editor
{
    // レベル選択用（エディタ上のみ、0-indexed）
    private int selectedLevelIndex = 0;
    private static readonly string[] levelLabels = { "Lv.1", "Lv.2", "Lv.3", "Lv.4", "Lv.5" };

    // シリアライズプロパティ
    private SerializedProperty keyProp;
    private SerializedProperty dungeonNameProp;
    private SerializedProperty dungeonDescriptionProp;
    private SerializedProperty dungeonImageProp;
    private SerializedProperty dungeonIconProp;
    private SerializedProperty dungeonNameImageProp;
    private SerializedProperty initDungeonLevelProp;
    private SerializedProperty recommendedLevelProp;
    private SerializedProperty difficultyProp;
    private SerializedProperty requiredAttributeProp;
    private SerializedProperty levelDataListProp;

    private void OnEnable()
    {
        keyProp = serializedObject.FindProperty("key");
        dungeonNameProp = serializedObject.FindProperty("dungeonName");
        dungeonDescriptionProp = serializedObject.FindProperty("dungeonDescription");
        dungeonImageProp = serializedObject.FindProperty("dungeonImage");
        dungeonIconProp = serializedObject.FindProperty("dungeonIcon");
        dungeonNameImageProp = serializedObject.FindProperty("dungeonNameImage");
        initDungeonLevelProp = serializedObject.FindProperty("initDungeonLevel");
        recommendedLevelProp = serializedObject.FindProperty("recommendedLevel");
        difficultyProp = serializedObject.FindProperty("difficulty");
        requiredAttributeProp = serializedObject.FindProperty("requiredAttribute");
        levelDataListProp = serializedObject.FindProperty("levelDataList");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ===== 基本情報 =====
        EditorGUILayout.LabelField("基本情報", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(keyProp);
        EditorGUILayout.PropertyField(dungeonNameProp);
        EditorGUILayout.Space();

        // ===== 表示情報 =====
        EditorGUILayout.LabelField("表示情報", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(dungeonDescriptionProp);
        EditorGUILayout.PropertyField(dungeonImageProp);
        EditorGUILayout.PropertyField(dungeonIconProp);
        EditorGUILayout.PropertyField(dungeonNameImageProp);
        EditorGUILayout.Space();

        // ===== レベル・難易度 =====
        EditorGUILayout.LabelField("レベル・難易度", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(initDungeonLevelProp);
        EditorGUILayout.PropertyField(recommendedLevelProp);
        EditorGUILayout.PropertyField(difficultyProp);
        EditorGUILayout.Space();

        // ===== 入場条件 =====
        EditorGUILayout.LabelField("入場条件", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(requiredAttributeProp);
        EditorGUILayout.Space();

        // ===== 敵データ（レベル別） =====
        EditorGUILayout.LabelField("敵データ（レベル別）", EditorStyles.boldLabel);

        // 配列サイズを5に固定
        EnsureLevelDataListSize();

        // レベル選択タブ
        EditorGUILayout.BeginHorizontal();
        selectedLevelIndex = GUILayout.Toolbar(selectedLevelIndex, levelLabels);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // 選択されたレベルのデータを表示
        if (levelDataListProp != null && selectedLevelIndex < levelDataListProp.arraySize)
        {
            var levelDataProp = levelDataListProp.GetArrayElementAtIndex(selectedLevelIndex);

            // ボックスで囲んで見やすくする
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"レベル {selectedLevelIndex + 1} の敵データ", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            var phasesProp = levelDataProp.FindPropertyRelative("phases");
            var monstersProp = levelDataProp.FindPropertyRelative("monsters");
            var bossNameProp = levelDataProp.FindPropertyRelative("bossName");
            var rewardGoldProp = levelDataProp.FindPropertyRelative("rewardGold");
            var levelUpCostProp = levelDataProp.FindPropertyRelative("levelUpCost");

            // ===== フェーズ構成（戦闘で実際に使われる） =====
            EditorGUILayout.LabelField("フェーズ構成（戦闘用）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "全フェーズをクリアするとダンジョンクリア。各フェーズの敵は最大3体ずつ同時出現し、倒すと残りが補充される。\n" +
                "ボスは最終フェーズの敵リストに isBoss の敵を入れる。", MessageType.None);

            if (phasesProp != null)
            {
                for (int p = 0; p < phasesProp.arraySize; p++)
                {
                    var phaseProp = phasesProp.GetArrayElementAtIndex(p);
                    var enemiesProp = phaseProp.FindPropertyRelative("enemies");

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    string suffix = p == phasesProp.arraySize - 1 ? "（最終・ボス可）" : "";
                    EditorGUILayout.LabelField($"フェーズ {p + 1}{suffix}", EditorStyles.boldLabel);
                    if (GUILayout.Button("削除", GUILayout.Width(44)))
                    {
                        phasesProp.DeleteArrayElementAtIndex(p);
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.PropertyField(enemiesProp, new GUIContent($"出現する敵（{enemiesProp.arraySize}体）"), true);
                    EditorGUILayout.EndVertical();
                }

                if (GUILayout.Button("＋ フェーズを追加"))
                {
                    phasesProp.arraySize++;
                    // 新規フェーズの敵リストを空にする（直前フェーズの内容が複製されるのを防ぐ）
                    var newPhase = phasesProp.GetArrayElementAtIndex(phasesProp.arraySize - 1);
                    var newEnemies = newPhase.FindPropertyRelative("enemies");
                    newEnemies.ClearArray();
                }
            }

            EditorGUILayout.Space(6);

            // ===== 旧方式（表示・クリア確率用） =====
            EditorGUILayout.LabelField("旧方式（ダンジョン情報画面の表示・クリア確率計算用）", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(monstersProp, new GUIContent("出現モンスター（表示用）"), true);
            EditorGUILayout.PropertyField(bossNameProp, new GUIContent("ボス名（旧方式）"));

            EditorGUILayout.Space(6);

            // ===== 報酬・費用 =====
            EditorGUILayout.LabelField("報酬・費用", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(rewardGoldProp, new GUIContent("勇者敗北時の報酬（G）"));
            EditorGUILayout.PropertyField(levelUpCostProp, new GUIContent("次レベルへの費用（G）"));

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space();


        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// levelDataList の配列サイズを5に固定する。
    /// </summary>
    private void EnsureLevelDataListSize()
    {
        if (levelDataListProp == null) return;

        if (levelDataListProp.arraySize != 5)
        {
            levelDataListProp.arraySize = 5;
        }
    }
}
#endif

