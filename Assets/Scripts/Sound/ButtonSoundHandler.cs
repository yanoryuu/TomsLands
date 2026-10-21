using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// アタッチした GameObject 以下の全 Button に操作音を自動登録する。
/// Canvas ルートにアタッチするだけでシーン内の全ボタンに対応する。
///
/// ・閉じる/戻る/キャンセル系のボタン（名前・ラベル・絵で判定）は「戻る」音、それ以外は決定音。
/// ・後から生成された行（一覧のスロット・セーブスロット等）も拾う（階層の数が変わったら登録し直す）。
/// ・同じボタンには一度しか登録しない（入れ子の Canvas に付いたハンドラ同士で二重に鳴らない）。
/// ・宴（Utage）の会話 UI のように自前で音を鳴らすボタンには付けない。
/// ・シーンにハンドラが無い Canvas には、ロード時に自動で付ける（<see cref="InstallOnSceneLoaded"/>）。
/// </summary>
[DisallowMultipleComponent]
public class ButtonSoundHandler : MonoBehaviour
{
    [SerializeField] private string seKey = UISound.DecideKey;
    [Tooltip("閉じる・戻る・キャンセルのボタンで鳴らす音")]
    [SerializeField] private string backSeKey = UISound.BackKey;

    private const float RescanInterval = 0.25f;

    // 登録済みのボタン（InstanceID）。全ハンドラで共有して二重登録を防ぐ
    private static readonly HashSet<int> Registered = new();

    private int lastHierarchyCount = -1;
    private float nextCheckTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Registered.Clear();

    private void Start() => Scan();

    private void LateUpdate()
    {
        float now = Time.unscaledTime;
        if (now < nextCheckTime) return;
        nextCheckTime = now + RescanInterval;
        if (transform.hierarchyCount != lastHierarchyCount) Scan();
    }

    private void Scan()
    {
        lastHierarchyCount = transform.hierarchyCount;
        RegisterButtons(GetComponentsInChildren<Button>(includeInactive: true));
    }

    /// <summary>動的に生成したボタンを追加登録する（登録済みのボタンは無視する）。</summary>
    public void RegisterButtons(Button[] buttons)
    {
        if (buttons == null) return;
        foreach (var button in buttons)
        {
            if (button == null || !Registered.Add(button.GetInstanceID())) continue;
            if (HasOwnSound(button)) continue;
            string key = IsBackButton(button) ? backSeKey : seKey;
            button.onClick.AddListener(() => UISound.Play(key));
        }
    }

    /// <summary>宴のボタン（UguiButtonSe で自前の音を鳴らす）。</summary>
    private static bool HasOwnSound(Button button) => button.GetComponent<Utage.UguiButtonSe>() != null;

    private static readonly string[] BackNameWords = { "close", "back", "cancel", "return", "exit" };
    private static readonly string[] BackLabels = { "閉じる", "とじる", "戻る", "もどる", "キャンセル", "いいえ", "やめる", "×", "✕" };
    private static readonly string[] BackSpriteWords = { "閉じる", "もどる", "戻る", "ばつ" };

    /// <summary>閉じる・戻る・キャンセルのボタンか（名前・ラベル・絵で判定）。</summary>
    private static bool IsBackButton(Button button)
    {
        // 「BackGround」（背景）は戻るではない
        string name = button.name.ToLowerInvariant().Replace("background", string.Empty);
        foreach (var w in BackNameWords)
            if (name.Contains(w)) return true;

        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            string text = label.text != null ? label.text.Trim() : string.Empty;
            foreach (var l in BackLabels)
                if (text == l) return true;
        }

        var image = button.targetGraphic as Image;
        var sprite = image != null ? image.sprite : null;
        if (sprite != null)
        {
            foreach (var w in BackSpriteWords)
                if (sprite.name.Contains(w)) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- シーン読み込み時の自動付与

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallOnSceneLoaded()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// ハンドラの無い Canvas（ポップアップ・設定・村・準備・リザルト・配信など）に付ける。
    /// 既に親か子にハンドラがある Canvas は、登録の重複ガードがあるので付けても鳴り方は変わらない。
    /// </summary>
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas == null) continue;
                // 一番外側の Canvas だけ（入れ子の Canvas は外側のハンドラが拾う）
                var parentCanvas = canvas.transform.parent != null ? canvas.transform.parent.GetComponentInParent<Canvas>(true) : null;
                if (parentCanvas != null) continue;
                if (canvas.GetComponentInParent<ButtonSoundHandler>(true) != null) continue;
                if (canvas.GetComponentInChildren<Button>(true) == null) continue;
                canvas.gameObject.AddComponent<ButtonSoundHandler>();
            }
        }
    }
}
