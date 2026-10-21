using UnityEngine;

/// <summary>
/// UI 操作音（決定・戻る）の窓口。
/// 同じフレームに UI 音が2回鳴らないようにする（入れ子の Canvas に付いた ButtonSoundHandler、
/// ボタン押下と B/Esc のキャンセル処理が同時に走る場合などの二重鳴りを防ぐ）。
/// SoundManager が無くてもエラーにならない。
/// </summary>
public static class UISound
{
    public const string DecideKey = "共通SE/SE_選択_決定";
    public const string BackKey = "共通SE/SE_選択_戻る・キャンセル";

    private static int lastFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => lastFrame = -1;

    /// <summary>決定音。</summary>
    public static void PlayDecide() => Play(DecideKey);

    /// <summary>戻る・閉じる・キャンセルの音。</summary>
    public static void PlayBack() => Play(BackKey);

    /// <summary>UI 音を鳴らす（このフレームで既に UI 音を鳴らしていれば鳴らさない）。</summary>
    public static void Play(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (Time.frameCount == lastFrame) return;
        // SoundManager は起動時（BeforeSceneLoad）に SoundManagerLoader が必ず生成する
        var soundManager = SoundManager.Instance;
        if (soundManager == null) return;
        lastFrame = Time.frameCount;
        soundManager.PlaySE(key);
    }
}
