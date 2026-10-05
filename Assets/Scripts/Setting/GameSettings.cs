using UnityEngine;

/// <summary>
/// プレイヤーのオプション設定（PlayerPrefs 永続化）。
/// セーブスロットに依存しない端末単位の設定なので PlayerPrefs を使う。
/// 各値は変更時に即保存され、次回起動時に SoundManager などから参照される。
/// </summary>
public static class GameSettings
{
    private const string KeyBgmVolume = "Settings.BgmVolume";
    private const string KeySeVolume = "Settings.SeVolume";
    private const string KeyFullscreen = "Settings.Fullscreen";
    private const string KeyResolutionWidth = "Settings.ResolutionWidth";
    private const string KeyResolutionHeight = "Settings.ResolutionHeight";
    private const string KeyFastEffects = "Settings.FastEffects";
    private const string KeyReduceShake = "Settings.ReduceShake";

    // ---- サウンド ----

    public static float BgmVolume
    {
        get => PlayerPrefs.GetFloat(KeyBgmVolume, 0.5f);
        set { PlayerPrefs.SetFloat(KeyBgmVolume, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    public static float SeVolume
    {
        get => PlayerPrefs.GetFloat(KeySeVolume, 1f);
        set { PlayerPrefs.SetFloat(KeySeVolume, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    /// <summary>BGM音量の保存値が存在するか（無ければ Inspector 値を使うための判定）。</summary>
    public static bool HasSavedBgmVolume => PlayerPrefs.HasKey(KeyBgmVolume);

    /// <summary>SE音量の保存値が存在するか（無ければ Inspector 値を使うための判定）。</summary>
    public static bool HasSavedSeVolume => PlayerPrefs.HasKey(KeySeVolume);

    // ---- 画面 ----

    public static bool Fullscreen
    {
        get => PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
        set { PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>保存済み解像度（未保存なら現在値）。</summary>
    public static Vector2Int Resolution
    {
        get => new(
            PlayerPrefs.GetInt(KeyResolutionWidth, Screen.width),
            PlayerPrefs.GetInt(KeyResolutionHeight, Screen.height));
        set
        {
            PlayerPrefs.SetInt(KeyResolutionWidth, value.x);
            PlayerPrefs.SetInt(KeyResolutionHeight, value.y);
            PlayerPrefs.Save();
        }
    }

    /// <summary>保存済みの画面設定を実際のウィンドウへ適用する。</summary>
    public static void ApplyScreenSettings()
    {
        var res = Resolution;
        Screen.SetResolution(res.x, res.y, Fullscreen);
    }

    // ---- 演出・快適性 ----

    /// <summary>true = 演出を速くする（ターン切替・カットイン・暗転など）。</summary>
    public static bool FastEffects
    {
        get => PlayerPrefs.GetInt(KeyFastEffects, 0) == 1;
        set { PlayerPrefs.SetInt(KeyFastEffects, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>演出時間に掛ける係数。通常=1.0 / 速い=0.5。</summary>
    public static float EffectDurationScale => FastEffects ? 0.5f : 1f;

    /// <summary>true = 画面シェイク演出を無効化する（アクセシビリティ）。</summary>
    public static bool ReduceShake
    {
        get => PlayerPrefs.GetInt(KeyReduceShake, 0) == 1;
        set { PlayerPrefs.SetInt(KeyReduceShake, value ? 1 : 0); PlayerPrefs.Save(); }
    }
}
