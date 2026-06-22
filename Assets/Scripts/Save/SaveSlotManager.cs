using System;
using System.IO;
using UnityEngine;

/// <summary>
/// セーブデータのスロット（最大3つ）を管理する。
///
/// 全セーブファイル（save.json / tomsData.json / itemData.json / heroData.json /
/// shopStatusData.json / streamingSelection.json）は
/// <c>persistentDataPath/slot_N/</c> 配下に保存され、本クラスがスロット別のパスを一元解決する。
///
/// 選択中スロットは PlayerPrefs に永続化されるため、タイトル→ゲームのシーン遷移をまたいで保持される。
/// 各モデルはコンストラクタでロードを行うため、<see cref="CurrentSlot"/> は
/// ゲームシーンをロードする「前」（＝タイトル側）で必ず確定させること。
/// </summary>
public static class SaveSlotManager
{
    /// <summary>保持できるスロットの最大数。</summary>
    public const int MaxSlots = 3;

    private const string CurrentSlotKey = "current_save_slot";

    /// <summary>スロットが使用済みか判定する基準ファイル（新規開始時に必ず書き込まれる）。</summary>
    private const string PresenceMarkerFile = "tomsData.json";

    private static int _currentSlot = -1;

    /// <summary>
    /// 選択中のスロット番号（0〜<see cref="MaxSlots"/>-1）。
    /// セッター時に PlayerPrefs へ永続化する。
    /// </summary>
    public static int CurrentSlot
    {
        get
        {
            if (_currentSlot < 0)
                _currentSlot = Mathf.Clamp(PlayerPrefs.GetInt(CurrentSlotKey, 0), 0, MaxSlots - 1);
            return _currentSlot;
        }
        set
        {
            _currentSlot = Mathf.Clamp(value, 0, MaxSlots - 1);
            PlayerPrefs.SetInt(CurrentSlotKey, _currentSlot);
            PlayerPrefs.Save();
        }
    }

    /// <summary>指定スロットのルートフォルダ。</summary>
    public static string SlotRoot(int slot)
        => Path.Combine(Application.persistentDataPath, $"slot_{Mathf.Clamp(slot, 0, MaxSlots - 1)}");

    /// <summary>選択中スロットのルートフォルダ。</summary>
    public static string CurrentRoot => SlotRoot(CurrentSlot);

    /// <summary>選択中スロット内のファイルパスを返す（フォルダは自動生成）。</summary>
    public static string GetPath(string fileName) => GetPath(CurrentSlot, fileName);

    /// <summary>指定スロット内のファイルパスを返す（フォルダは自動生成）。</summary>
    public static string GetPath(int slot, string fileName)
    {
        var root = SlotRoot(slot);
        Directory.CreateDirectory(root);
        return Path.Combine(root, fileName);
    }

    /// <summary>指定スロットにセーブデータが存在するか。</summary>
    public static bool Exists(int slot)
        => File.Exists(Path.Combine(SlotRoot(slot), PresenceMarkerFile));

    /// <summary>いずれかのスロットにセーブデータが存在するか。</summary>
    public static bool AnyExists()
    {
        for (int i = 0; i < MaxSlots; i++)
            if (Exists(i)) return true;
        return false;
    }

    /// <summary>指定スロットのセーブデータをフォルダごと削除する。</summary>
    public static void DeleteSlot(int slot)
    {
        var root = SlotRoot(slot);
        if (!Directory.Exists(root)) return;

        try
        {
            Directory.Delete(root, recursive: true);
#if UNITY_EDITOR
            Debug.Log($"[SaveSlotManager] スロット{slot}のセーブデータを削除しました: {root}");
#endif
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveSlotManager] スロット{slot}の削除に失敗: {e.Message}");
        }
    }

    /// <summary>
    /// タイトルのスロット表示用に、保存サマリ（日数・所持金・難易度）を読み取る。
    /// 各モデルを完全にロードせず tomsData.json のみ参照する軽量な処理。
    /// </summary>
    public static SaveSlotInfo GetSlotInfo(int slot)
    {
        var info = new SaveSlotInfo { SlotIndex = slot, Exists = false };
        var path = Path.Combine(SlotRoot(slot), PresenceMarkerFile);
        if (!File.Exists(path)) return info;

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<TomsData>(json);
            if (data != null)
            {
                info.Exists = true;
                info.Day = data.currentTurn;
                info.Gold = data.shopMoney;
                info.Mode = (GameModeId)data.gameMode;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveSlotManager] スロット{slot}のサマリ読み取りに失敗: {e.Message}");
        }

        return info;
    }
}
