using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// サウンド管理クラス
/// Resources/Sounds/BGM, Resources/Sounds/SE フォルダからクリップを名前指定で再生する
/// </summary>
public class SoundManager : SingletonMonoBehaviour<SoundManager>
{
    [Header("BGM")]
    [SerializeField, Range(0f, 1f)] private float bgmVolume = 0.5f;
    [Header("SE")]
    [SerializeField, Range(0f, 1f)] private float seVolume = 1.0f;
    [Header("同時再生可能なSE数")]
    [SerializeField] private int maxSeSources = 8;

    private AudioSource bgmSource;
    private List<AudioSource> seSources = new List<AudioSource>();

    // キャッシュ
    private Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();

    private const string BGM_PATH = "Sounds/BGM/";
    private const string SE_PATH  = "Sounds/SE/";

    protected override void Awake()
    {
        base.Awake();

        // BGM用AudioSource
        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
        bgmSource.volume = bgmVolume;

        // SE用AudioSource
        for (int i = 0; i < maxSeSources; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.loop = false;
            src.playOnAwake = false;
            src.volume = seVolume;
            seSources.Add(src);
        }
    }

    // ---- BGM ----

    /// <summary>BGMを再生（名前指定）</summary>
    public void PlayBGM(string name, float fadeInTime = 0f)
    {
        var clip = LoadClip(BGM_PATH + name);
        if (clip == null) return;

        if (bgmSource.clip == clip && bgmSource.isPlaying) return;

        bgmSource.clip = clip;
        bgmSource.volume = fadeInTime > 0f ? 0f : bgmVolume;
        bgmSource.Play();

        if (fadeInTime > 0f)
            StartCoroutine(FadeAudio(bgmSource, bgmVolume, fadeInTime));
    }

    /// <summary>BGMを停止</summary>
    public void StopBGM(float fadeOutTime = 0f)
    {
        if (!bgmSource.isPlaying) return;

        if (fadeOutTime > 0f)
            StartCoroutine(FadeAudio(bgmSource, 0f, fadeOutTime, () => bgmSource.Stop()));
        else
            bgmSource.Stop();
    }

    /// <summary>BGMを一時停止/再開</summary>
    public void PauseBGM()  => bgmSource.Pause();
    public void ResumeBGM() => bgmSource.UnPause();

    // ---- SE ----

    /// <summary>SEを再生（名前指定）</summary>
    public void PlaySE(string name)
    {
        var clip = LoadClip(SE_PATH + name);
        if (clip == null) return;

        var src = GetAvailableSESource();
        if (src == null) return;

        src.clip = clip;
        src.volume = seVolume;
        src.Play();
    }

    /// <summary>全SEを停止</summary>
    public void StopAllSE()
    {
        foreach (var src in seSources) src.Stop();
    }

    // ---- Volume ----

    public void SetBGMVolume(float vol)
    {
        bgmVolume = Mathf.Clamp01(vol);
        bgmSource.volume = bgmVolume;
    }

    public void SetSEVolume(float vol)
    {
        seVolume = Mathf.Clamp01(vol);
        foreach (var src in seSources) src.volume = seVolume;
    }

    public float GetBGMVolume() => bgmVolume;
    public float GetSEVolume()  => seVolume;

    // ---- Internal ----

    private AudioClip LoadClip(string path)
    {
        if (clipCache.TryGetValue(path, out var cached)) return cached;

        var clip = Resources.Load<AudioClip>(path);
        if (clip == null)
        {
            Debug.LogWarning($"[SoundManager] AudioClip not found: {path}");
            return null;
        }
        clipCache[path] = clip;
        return clip;
    }

    private AudioSource GetAvailableSESource()
    {
        foreach (var src in seSources)
            if (!src.isPlaying) return src;

        // 全て使用中なら最も再生時間が長いものを再利用
        AudioSource oldest = seSources[0];
        foreach (var src in seSources)
            if (src.time > oldest.time) oldest = src;
        return oldest;
    }

    private System.Collections.IEnumerator FadeAudio(AudioSource source, float targetVol, float duration, System.Action onComplete = null)
    {
        float startVol = source.volume;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            source.volume = Mathf.Lerp(startVol, targetVol, elapsed / duration);
            yield return null;
        }
        source.volume = targetVol;
        onComplete?.Invoke();
    }

    /// <summary>キャッシュをクリア</summary>
    public void ClearCache()
    {
        clipCache.Clear();
    }
}
