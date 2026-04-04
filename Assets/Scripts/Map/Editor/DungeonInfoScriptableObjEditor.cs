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
    private SerializedProperty currentDungeonLevelProp;
    private SerializedProperty rewardGoldProp;
    private SerializedProperty dungeonStatusProp;

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
        currentDungeonLevelProp = serializedObject.FindProperty("currentDungeonLevel");
        rewardGoldProp = serializedObject.FindProperty("rewardGold");
        dungeonStatusProp = serializedObject.FindProperty("dungeonStatus");
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

            var monstersProp = levelDataProp.FindPropertyRelative("monsters");
            var bossNameProp = levelDataProp.FindPropertyRelative("bossName");

            EditorGUILayout.PropertyField(monstersProp, new GUIContent("出現モンスター"), true);
            EditorGUILayout.PropertyField(bossNameProp, new GUIContent("ボス名"));

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space();

        // ===== 進行状況 =====
        EditorGUILayout.LabelField("進行状況", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(currentDungeonLevelProp);
        EditorGUILayout.Space();

        // ===== 報酬 =====
        EditorGUILayout.LabelField("魔王軍勝利時の報酬", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(rewardGoldProp);
        EditorGUILayout.Space();

        // ===== ダンジョン状態 =====
        EditorGUILayout.LabelField("ダンジョンの状態", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(dungeonStatusProp);

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

