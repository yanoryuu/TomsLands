using UnityEngine;

/// <summary>
/// セーブデータの版番号（tomsData.json と metaData.json の saveVersion）。
///
/// - 版を入れる前のセーブは saveVersion が欠損し、JsonUtility で 0 として読まれる（＝版 0）。
/// - セーブの中身の意味を変えたら <see cref="Current"/> を上げ、<see cref="Migrate"/> に古い版からの読み替えを足す。
///   （フィールドの追加だけなら上げなくてよい。欠損時の既定値で読めるため）
/// </summary>
public static class SaveVersion
{
    /// <summary>
    /// 現在の版。
    /// 1: 版番号を追加。表示待ちのイベント（tomsData）・レリック3択の保留（relics）・進行中のバズ（shopStatus）を保存。
    /// </summary>
    public const int Current = 1;

    /// <summary>
    /// 読み込んだ版を現在の版へそろえる入口。今は版 0→1 で読み替えが要る項目は無い
    /// （追加した項目は欠損時の既定値で正しく読める）ので、ログだけ出す。
    /// </summary>
    /// <returns>読み替え後の版（常に Current）。</returns>
    public static int Migrate(int loadedVersion, string fileName)
    {
        if (loadedVersion > Current)
        {
            // 新しいビルドで保存したセーブを古いビルドで開いた。知らない項目は捨てられる
            Debug.LogWarning($"[SaveVersion] {fileName} は新しい版({loadedVersion})のセーブです（このビルドは版{Current}）。読める範囲で続行します。");
            return Current;
        }
        if (loadedVersion < Current)
            Debug.Log($"[SaveVersion] {fileName} を版{loadedVersion}から版{Current}として読みます。");
        return Current;
    }
}
