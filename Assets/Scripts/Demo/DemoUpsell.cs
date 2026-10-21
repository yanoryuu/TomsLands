#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
#define TOMS_STEAM
#endif

using System;
using UnityEngine;
#if TOMS_STEAM
using Steamworks;
#endif

/// <summary>
/// 体験版のラン終了後に出す「製品版へ」画面（既存のポップアップをそのまま使う。新しい素材は作らない）。
/// そのランの儲けとランクを出し、製品版のストアページ（Steam のオーバーレイ、使えなければブラウザ）を開くボタンと、
/// タイトルへ戻るボタンを置く。ストアを開いてもポップアップは閉じない（戻ってきてからタイトルへ）。
/// </summary>
public static class DemoUpsell
{
    /// <summary>
    /// 「製品版へ」のポップアップを出す。popUpManager が無いとき（未配線）はそのままタイトルへ進む。
    /// </summary>
    /// <param name="cleared">クリア（リザルト）なら true、破産なら false。</param>
    /// <param name="profit">そのランの儲け（リザルトと同じ式。破産はこのランの最高の儲け）。</param>
    /// <param name="rank">ランク（S〜D）。破産は空。</param>
    /// <param name="goToTitle">タイトルへ戻る処理。</param>
    public static void Show(PopUpManager popUpManager, bool cleared, int profit, string rank, Action goToTitle)
    {
        if (popUpManager == null)
        {
            Debug.LogWarning("[DemoUpsell] PopUpManager が無いので、製品版へのポップアップを出さずにタイトルへ戻ります。");
            goToTitle?.Invoke();
            return;
        }

        popUpManager.Show(new PopUpData
        {
            Title = "体験版はここまで",
            Message = BuildMessage(cleared, profit, rank),
            ConfirmButtonText = "ストアページへ",
            CancelButtonText = "タイトルへ",
            Size = PopupSizeEnum.Medium,
            KeepOpenOnConfirm = true,
            OnConfirm = OpenStorePage,
            OnCancel = goToTitle,
        });
        Debug.Log($"[DemoUpsell] 製品版へ: {(cleared ? "クリア" : "破産")} 儲け {profit}G ランク {(string.IsNullOrEmpty(rank) ? "-" : rank)}");
    }

    /// <summary>ポップアップの本文。</summary>
    public static string BuildMessage(bool cleared, int profit, string rank)
    {
        string sign = profit >= 0 ? "+" : "−";
        string rankText = cleared && !string.IsNullOrEmpty(rank) ? rank : "なし（破産）";
        return $"今回の儲け　{sign}{Mathf.Abs(profit):N0}G\nランク　{rankText}\n\n" +
               "製品版では、村を育てると長い商いに出られます。";
    }

    /// <summary>
    /// 製品版のストアページを開く。Steam のオーバーレイが使えればオーバーレイで、使えなければブラウザで開く。
    /// </summary>
    public static void OpenStorePage()
    {
#if TOMS_STEAM
        try
        {
            if (SteamManager.Initialized && SteamUtils.IsOverlayEnabled())
            {
                SteamFriends.ActivateGameOverlayToStore(new AppId_t(DemoBuild.FullGameAppId), EOverlayToStoreFlag.k_EOverlayToStoreFlag_None);
                Debug.Log($"[DemoUpsell] Steam のオーバーレイでストアページを開きました (App {DemoBuild.FullGameAppId})");
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DemoUpsell] オーバーレイで開けなかったのでブラウザで開きます: {e.Message}");
        }
#endif
        Application.OpenURL(DemoBuild.FullGameStoreUrl);
        Debug.Log($"[DemoUpsell] ブラウザでストアページを開きました: {DemoBuild.FullGameStoreUrl}");
    }
}
