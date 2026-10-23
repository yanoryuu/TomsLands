using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 配信値で上書きした ScriptableObject の複製を、元のアセット1つにつき1つだけ持つ（RemoteBalance・ItemMaster 用）。
///
/// 複製は C# の参照からしか持たれないので、そのままだとシーン読み込み時の UnloadUnusedAssets で破棄され、
/// 中身が null（破棄済み）に化ける。ビルドでは LifetimeScope.Configure の後にこの破棄が走ることがあり、
/// 配信画面でダンジョンもアイテムのマスターも「見つからない」になって進行不能になっていた。
/// そこで複製はここで持ち主を決めて破棄の対象から外し、呼ぶたびに作らず使い回す（シーンをまたいでも増えない）。
/// 配信値を取り込み直したときは <see cref="Clear"/> で破棄して作り直す。
/// </summary>
public sealed class OverrideCloneCache
{
    private readonly Dictionary<(string section, Object original), Object> _clones = new();

    /// <summary>
    /// original の上書き済み複製を返す。初回だけ複製して apply で上書きする。
    /// apply が false を返したら複製を捨てて original を使う（以降も original を返す）。
    /// </summary>
    public T GetOrCreate<T>(string section, T original, Func<T, bool> apply) where T : ScriptableObject
    {
        var key = (section, (Object)original);
        if (_clones.TryGetValue(key, out var cached) && cached != null) return (T)cached;

        RemoveUnloadedOriginals();

        var clone = Object.Instantiate(original);
        clone.name = original.name; // (Clone) サフィックス回避
        T result = clone;
        if (apply(clone))
        {
            clone.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }
        else
        {
            DestroyObject(clone);
            result = original;
        }
        _clones[key] = result;
        return result;
    }

    /// <summary>持っている複製をすべて破棄する（配信値を取り込み直したとき）。</summary>
    public void Clear()
    {
        foreach (var kv in _clones) DestroyClone(kv.Key.original, kv.Value);
        _clones.Clear();
    }

    // 元のアセット（Addressables）が解放されて読み直されると別インスタンスになるので、古い組は捨てる
    private void RemoveUnloadedOriginals()
    {
        List<(string, Object)> stale = null;
        foreach (var kv in _clones)
        {
            if (kv.Key.original != null) continue;
            DestroyClone(kv.Key.original, kv.Value);
            (stale ??= new()).Add(kv.Key);
        }
        if (stale != null) foreach (var key in stale) _clones.Remove(key);
    }

    private static void DestroyClone(Object original, Object clone)
    {
        // 配信値を諦めた要素は original そのものを持っているので、アセットを壊さないよう破棄しない
        if (clone == null || ReferenceEquals(clone, original)) return;
        DestroyObject(clone);
    }

    private static void DestroyObject(Object obj)
    {
        if (Application.isPlaying) Object.Destroy(obj);
        else Object.DestroyImmediate(obj); // エディタのツール（AutoPlay 等）から呼ばれたとき
    }
}
