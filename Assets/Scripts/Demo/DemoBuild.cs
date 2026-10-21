using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 体験版（スクリプト定義 DEMO_BUILD）の切り替えと定数を1か所にまとめる（Docs/Demo_Build.md）。
///
/// 体験版の違い:
///   ・遊べるのは かんたん（Short）の1ランだけ。村・準備シーン・ふつう／長いランには進めない（タイトル → 店に直行）
///   ・ラン終了後（クリア・破産とも）は村ではなく「製品版へ」のポップアップ → タイトルへ
///   ・セーブ・metaData・実績の手元記録は製品版と別のフォルダ（<see cref="DataRoot"/>）。製品版へは引き継がない
///   ・本編の Steam 実績は解除処理も呼ばない
///
/// 定義の切り替えは Tools/TomsLands/体験版/ のメニュー（Editor/DemoBuildMenu.cs）。
/// </summary>
public static class DemoBuild
{
    /// <summary>体験版のビルドか。const にしないのは、到達しないコードの警告（CS0162）を出さないため。</summary>
    public static bool IsDemo
    {
        get
        {
#if DEMO_BUILD
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// 体験版の Steam App ID（トコのトムトムランド 体験版、2026-10-22 取得）。
    /// 変えるときは SteamPipe の VDF（tools/steampipe/scripts/*_demo.vdf）も合わせる（Docs/Demo_Build.md）。
    /// 0 にすると開発ビルドにも steam_appid.txt を入れない。Steam の初期化に失敗しても体験版はそのまま動く。
    /// </summary>
    public const uint DemoSteamAppId = 5186790;

    /// <summary>製品版（トコのトムトムランド）の Steam App ID。「製品版へ」で開くストアページ。</summary>
    public const uint FullGameAppId = 4535810;

    /// <summary>オーバーレイが使えないときにブラウザで開く製品版のストアページ。</summary>
    public const string FullGameStoreUrl = "https://store.steampowered.com/app/4535810/";

    /// <summary>
    /// 体験版の保存先フォルダ名（persistentDataPath の会社名フォルダの下。製品版は TomSLand）。
    /// 体験版のビルドは productName もこの名前にする（Player.log・PlayerPrefs も分かれる）。
    /// </summary>
    public const string DemoProductName = "TomSLand_Demo";

    /// <summary>
    /// 体験版が読むリモート配信の環境（URL の config/&lt;ここ&gt;/）。製品版と同じ production を読む。
    /// 体験版だけ値を変えたくなったら、サーバーに config/demo/ を作ってここを "demo" にする（Docs/Demo_Build.md）。
    /// </summary>
    public const string DemoRemoteConfigEnvironment = "production";

    /// <summary>体験版の App ID が決まっているか（const を直接比べると到達しないコードの警告が出るのでプロパティにする）。</summary>
    public static bool HasDemoSteamAppId => DemoSteamAppId != 0;

    /// <summary>今動いている版の Steam App ID（体験版で未取得なら 0）。</summary>
    public static uint SteamAppId => IsDemo ? DemoSteamAppId : SteamManager.AppId;

    private static string dataRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => dataRoot = null;

    /// <summary>
    /// セーブ・metaData・実績の手元記録・リモート配信のキャッシュを置く根のフォルダ。
    /// 製品版は persistentDataPath（…/MozaicWorks/TomSLand）のまま。
    /// 体験版は隣の …/MozaicWorks/TomSLand_Demo（体験版のビルドは productName が TomSLand_Demo なので persistentDataPath そのもの。
    /// エディタで DEMO_BUILD を入れて再生したときも同じ場所になる）。
    /// </summary>
    public static string DataRoot
    {
        get
        {
            if (!IsDemo) return Application.persistentDataPath;
            if (dataRoot != null) return dataRoot;

            string persistent = Application.persistentDataPath;
            string trimmed = persistent.TrimEnd('/', '\\');
            if (string.Equals(Path.GetFileName(trimmed), DemoProductName, StringComparison.OrdinalIgnoreCase))
            {
                dataRoot = persistent;
            }
            else
            {
                string parent = Path.GetDirectoryName(trimmed);
                dataRoot = string.IsNullOrEmpty(parent) ? Path.Combine(persistent, DemoProductName) : Path.Combine(parent, DemoProductName);
            }
            return dataRoot;
        }
    }

    /// <summary>リモート配信の URL を体験版の環境に差し替える（製品版・production のままなら何もしない）。</summary>
    public static string RemoteConfigUrl(string url)
    {
        if (!IsDemo || string.IsNullOrEmpty(url) || DemoRemoteConfigEnvironment == "production") return url;
        return url.Replace("/config/production/", $"/config/{DemoRemoteConfigEnvironment}/");
    }
}
