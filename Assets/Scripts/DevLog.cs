using System.Diagnostics;

/// <summary>
/// 開発中だけ出す細かいログ（SE の再生・保存のたびのログなど）。
/// 製品ビルドでは呼び出しごとコンパイルから消える（引数の文字列も作られない）。
/// 製品ビルドの Player.log を不具合報告で読める長さに保つため（2026-10-20）。
/// </summary>
public static class DevLog
{
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(string message) => UnityEngine.Debug.Log(message);
}
