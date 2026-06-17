using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Resources.Load を置き換えるための Addressables 同期ロードラッパ。
/// VContainer の Configure() など同期コンテキストから呼べるよう WaitForCompletion で同期化している。
/// 取得したアセットはアプリ寿命の間保持する想定のため、ここでは Release しない。
/// （旧 Resources.Load と同じく、明示解放しないマスターデータ／シーン間共有データ向け）
/// </summary>
public static class AddressableLoader
{
    /// <summary>
    /// 単体アセットをアドレス指定で同期ロードする。
    /// </summary>
    public static T Load<T>(string address) where T : Object
    {
        var handle = Addressables.LoadAssetAsync<T>(address);
        var result = handle.WaitForCompletion();
        if (result == null)
            Debug.LogError($"[AddressableLoader] アドレス '{address}' のロードに失敗しました。");
        return result;
    }

    /// <summary>
    /// ラベルに紐づく複数アセットを同期ロードする（旧 Resources.LoadAll の置き換え）。
    /// </summary>
    public static List<T> LoadAll<T>(string label) where T : Object
    {
        var handle = Addressables.LoadAssetsAsync<T>(label, null);
        var result = handle.WaitForCompletion();
        return result != null ? new List<T>(result) : new List<T>();
    }
}
