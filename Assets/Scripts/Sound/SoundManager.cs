using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// サウンド管理クラス。
/// Awake 時に SoundLibrary の全クリップを同期プリロードするため、
/// ゲーム開始後はどのタイミングでも即時再生できる。
/// </summary>
public class SoundManager : SingletonMonoBehaviour<SoundManager>
{
    [SerializeField] private SoundLibrary library;

    [Header("BGM")]
    [SerializeField, Range(0f, 1f)] private float bgmVolume = 0.5f;
    [Header("SE")]
    [SerializeField, Range(0f, 1f)] private float seVolume = 1.0f;
    [Header("同時再生可能なSE数")]
    [SerializeField] private int maxSeSources = 8;

    private AudioSource bgmSource;
    private readonly List<AudioSource> seSources = new();

    // GUID → ロード済みハンドル
    private readonly Dictionary<string, AsyncOperationHandle<AudioClip>> handleCache = new();

    protected override void Awake()
    {
        base.Awake();

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
        bgmSource.volume = bgmVolume;

        for (int i = 0; i < maxSeSources; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.loop = false;
            src.playOnAwake = false;
            src.volume = seVolume;
            seSources.Add(src);
        }

        // 同期プリロード: Awake 完了時点で全クリップがメモリに載っている
        PreloadAll();
    }

    private void OnDestroy()
    {
        foreach (var handle in handleCache.Values)
        {
            if (handle.IsValid())
                Addressables.Release(handle);
        }
        handleCache.Clear();
    }

    // ---- Preload ----

    /// <summary>
    /// SoundLibrary に登録された全クリップを同期プリロードする。
    /// WaitForCompletion により Awake 内でロードを完結させる。
    /// </summary>
    private void PreloadAll()
    {
        if (library == null)
        {
            Debug.LogWarning("[SoundManager] SoundLibrary が未設定です。");
            return;
        }

        var allEntries = new List<SoundLibrary.Entry>(library.bgmList.Count + library.seList.Count);
        allEntries.AddRange(library.bgmList);
        allEntries.AddRange(library.seList);

        foreach (var entry in allEntries)
        {
            if (!IsValidRef(entry?.clipRef)) continue;
            string guid = entry.clipRef.AssetGUID;
            if (handleCache.ContainsKey(guid)) continue;

            var handle = Addressables.LoadAssetAsync<AudioClip>(entry.clipRef);
            handle.WaitForCompletion(); // 同期待機
            handleCache[guid] = handle;

            if (handle.Status != AsyncOperationStatus.Succeeded)
                Debug.LogWarning($"[SoundManager] ロード失敗: {entry.key} ({guid})");
        }

        Debug.Log($"[SoundManager] プリロード完了: {handleCache.Count} クリップ");
    }

    // ---- BGM ----

    /// <summary>BGM を再生する（キー名で指定）</summary>
    public void PlayBGM(string key, float fadeInTime = 0f)
    {
        var clip = FindClip(library?.FindBGM(key), key, isBGM: true);
        if (clip == null) return;

        if (bgmSource.clip == clip && bgmSource.isPlaying) return;

        bgmSource.clip = clip;
        bgmSource.volume = fadeInTime > 0f ? 0f : bgmVolume;
        bgmSource.Play();
        
        
        Debug.Log($"[SoundManager] BGM 再生: {key} (fadeIn: {fadeInTime}s)");

        if (fadeInTime > 0f)
            StartCoroutine(FadeAudio(bgmSource, bgmVolume, fadeInTime));
    }

    /// <summary>BGM を停止する</summary>
    public void StopBGM(float fadeOutTime = 0f)
    {
        if (!bgmSource.isPlaying) return;
        if (fadeOutTime > 0f)
            StartCoroutine(FadeAudio(bgmSource, 0f, fadeOutTime, () => bgmSource.Stop()));
        else
            bgmSource.Stop();
    }

    public void PauseBGM()  => bgmSource.Pause();
    public void ResumeBGM() => bgmSource.UnPause();

    // ---- SE ----

    /// <summary>SE を再生する（キー名で指定）</summary>
    public void PlaySE(string key)
    {
        var clip = FindClip(library?.FindSE(key), key, isBGM: false);
        if (clip == null) return;

        var src = GetAvailableSESource();
        if (src == null) return;

        src.clip = clip;
        src.volume = seVolume;
        src.Play();
        
        Debug.Log($"[SoundManager] SE 再生: {key}");
    }

    /// <summary>再生中の全 SE を停止する</summary>
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

    private AudioClip FindClip(SoundLibrary.Entry entry, string key, bool isBGM)
    {
        if (entry == null)
        {
            var list = isBGM ? library?.bgmList : library?.seList;
            var registered = list != null && list.Count > 0
                ? string.Join(", ", list.ConvertAll(e => $"'{e.key}'"))
                : "（未登録）";
            Debug.LogWarning($"[SoundManager] {(isBGM ? "BGM" : "SE")} キーが見つかりません: '{key}'\n登録済みキー: {registered}");
            return null;
        }

        string guid = entry.clipRef.AssetGUID;
        if (!handleCache.TryGetValue(guid, out var handle)
            || handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogWarning($"[SoundManager] クリップ未ロード: {key}");
            return null;
        }

        return handle.Result;
    }

    private AudioSource GetAvailableSESource()
    {
        foreach (var src in seSources)
            if (!src.isPlaying) return src;

        // 全て使用中なら最も再生が進んでいるものを再利用
        var oldest = seSources[0];
        foreach (var src in seSources)
            if (src.time > oldest.time) oldest = src;
        return oldest;
    }

    private IEnumerator FadeAudio(AudioSource source, float targetVol, float duration, System.Action onComplete = null)
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

    private static bool IsValidRef(AssetReferenceT<AudioClip> clipRef)
        => clipRef != null && clipRef.RuntimeKeyIsValid();
}
