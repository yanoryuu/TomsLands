using System;
using UnityEngine;

/// <summary>
/// プレイヤーのオプション設定（PlayerPrefs 永続化）。
/// セーブスロットに依存しない端末単位の設定なので PlayerPrefs を使う。
/// 値は変えた時点で PlayerPrefs に入れ、ディスクへの書き出し（PlayerPrefs.Save）は設定画面を閉じるときにまとめて行う（<see cref="Save"/>）。
///
/// 画面（表示モード・解像度）はここでは持たない。Unity が自分で保存して次の起動で使うので、二重に管理しない
/// （設定画面は Screen.fullScreenMode / Screen.width・height を直接読み書きする）。
/// </summary>
public static class GameSettings
{
    private const string KeyBgmVolume = "Settings.BgmVolume";
    private const string KeySeVolume = "Settings.SeVolume";
    private const string KeyFastEffects = "Settings.FastEffects";
    private const string KeyReduceShake = "Settings.ReduceShake";
    private const string KeyTextSpeed = "Settings.TextSpeed";
    private const string KeyAutoAdvance = "Settings.AutoAdvance";
    private const string KeySkipRead = "Settings.SkipRead";

    /// <summary>どれかの設定が変わった（会話の設定・SE 音量を宴へ反映するのに使う）。</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Changed = null;

    /// <summary>変更をディスクへ書き出す（設定画面を閉じるときに呼ぶ）。</summary>
    public static void Save() => PlayerPrefs.Save();

    // ---- サウンド ----

    public static float BgmVolume
    {
        get => PlayerPrefs.GetFloat(KeyBgmVolume, 0.5f);
        set { PlayerPrefs.SetFloat(KeyBgmVolume, Mathf.Clamp01(value)); Changed?.Invoke(); }
    }

    public static float SeVolume
    {
        get => PlayerPrefs.GetFloat(KeySeVolume, 1f);
        set { PlayerPrefs.SetFloat(KeySeVolume, Mathf.Clamp01(value)); Changed?.Invoke(); }
    }

    /// <summary>BGM音量の保存値が存在するか（無ければ Inspector 値を使うための判定）。</summary>
    public static bool HasSavedBgmVolume => PlayerPrefs.HasKey(KeyBgmVolume);

    /// <summary>SE音量の保存値が存在するか（無ければ Inspector 値を使うための判定）。</summary>
    public static bool HasSavedSeVolume => PlayerPrefs.HasKey(KeySeVolume);

    // ---- 演出・快適性 ----

    /// <summary>true = 演出を速くする（ターン切替・カットイン・暗転など）。</summary>
    public static bool FastEffects
    {
        get => PlayerPrefs.GetInt(KeyFastEffects, 0) == 1;
        set { PlayerPrefs.SetInt(KeyFastEffects, value ? 1 : 0); Changed?.Invoke(); }
    }

    /// <summary>演出時間に掛ける係数。通常=1.0 / 速い=0.5。</summary>
    public static float EffectDurationScale => FastEffects ? 0.5f : 1f;

    /// <summary>true = 画面シェイク演出を無効化する（アクセシビリティ）。</summary>
    public static bool ReduceShake
    {
        get => PlayerPrefs.GetInt(KeyReduceShake, 0) == 1;
        set { PlayerPrefs.SetInt(KeyReduceShake, value ? 1 : 0); Changed?.Invoke(); }
    }

    // ---- 会話（宴） ----

    /// <summary>会話の文字の速さ（0=遅い … 1=瞬時）。既定は宴の既定値と同じ 0.5。</summary>
    public static float TextSpeed
    {
        get => PlayerPrefs.GetFloat(KeyTextSpeed, 0.5f);
        set { PlayerPrefs.SetFloat(KeyTextSpeed, Mathf.Clamp01(value)); Changed?.Invoke(); }
    }

    /// <summary>true = 会話を自動で送る（オート）。</summary>
    public static bool AutoAdvance
    {
        get => PlayerPrefs.GetInt(KeyAutoAdvance, 0) == 1;
        set { PlayerPrefs.SetInt(KeyAutoAdvance, value ? 1 : 0); Changed?.Invoke(); }
    }

    /// <summary>true = 一度読んだ会話を早送りする（既読スキップ。未読のところで止まる）。</summary>
    public static bool SkipRead
    {
        get => PlayerPrefs.GetInt(KeySkipRead, 0) == 1;
        set { PlayerPrefs.SetInt(KeySkipRead, value ? 1 : 0); Changed?.Invoke(); }
    }
}
