using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// フォロワーシステム。
/// フォロワー数に応じたマイルストーン判定と、各種ボーナスの計算を担当する。
/// 
/// 【既存システムとの連携】
/// - ShopStatusModel のフォロワー数を参照してマイルストーンを判定する。
/// - BuzzSystem からバズ確率ボーナスを参照される。
/// - SalesCalculator から売上ボーナスを参照される。
/// - AdvertisementSystem から広告費割引率を参照される。
/// </summary>
public class FollowerSystem
{
    private readonly ShopStatusModel _statusModel;
    private readonly List<FollowerMilestoneData> _milestones;

    /// <summary>
    /// コンストラクタ。VContainer から注入する。
    /// </summary>
    /// <param name="statusModel">ステータスモデル（フォロワー数を参照）</param>
    /// <param name="milestones">マイルストーンデータのリスト（Resources からロードして登録）</param>
    public FollowerSystem(ShopStatusModel statusModel, List<FollowerMilestoneData> milestones)
    {
        _statusModel = statusModel;
        _milestones = milestones;

        if (_milestones == null || _milestones.Count == 0)
        {
            Debug.LogWarning("[FollowerSystem] マイルストーンデータが未設定です。");
            _milestones = new List<FollowerMilestoneData>();
        }
        else
        {
            // 必要フォロワー数の昇順にソートしておく
            _milestones = _milestones.OrderBy(m => m.requiredFollowers).ToList();
        }
    }

    /// <summary>
    /// 現在のフォロワー数で達成している最高のマイルストーンを取得する。
    /// 達成しているマイルストーンがない場合は null を返す。
    /// </summary>
    public FollowerMilestoneData GetCurrentMilestone()
    {
        if (_milestones == null || _milestones.Count == 0) return null;

        int followers = _statusModel.Followers.Value;
        FollowerMilestoneData bestMilestone = null;

        // 昇順にソートされているので、条件を満たす最後のものが最高マイルストーン
        foreach (var milestone in _milestones)
        {
            if (milestone == null) continue;
            if (followers >= milestone.requiredFollowers)
            {
                bestMilestone = milestone;
            }
            else
            {
                break; // これ以降は達成不可能
            }
        }

        return bestMilestone;
    }

    /// <summary>
    /// 次に達成すべきマイルストーンを取得する。
    /// 全て達成済みの場合は null を返す。
    /// </summary>
    public FollowerMilestoneData GetNextMilestone()
    {
        if (_milestones == null || _milestones.Count == 0) return null;

        int followers = _statusModel.Followers.Value;

        foreach (var milestone in _milestones)
        {
            if (milestone == null) continue;
            if (followers < milestone.requiredFollowers)
            {
                return milestone;
            }
        }

        return null; // 全マイルストーン達成済み
    }

    /// <summary>
    /// 現在の売上ボーナス率を取得する（0〜、例: 0.15 = 15%UP）。
    /// マイルストーン未達成の場合は 0 を返す。
    /// </summary>
    public float GetSalesBonusRate()
    {
        var milestone = GetCurrentMilestone();
        return milestone != null ? milestone.salesBonusRate : 0f;
    }

    /// <summary>
    /// 現在のバズ確率ボーナスを取得する（%単位、例: 5.0 = +5%）。
    /// マイルストーン未達成の場合は 0 を返す。
    /// </summary>
    public float GetBuzzChanceBonus()
    {
        var milestone = GetCurrentMilestone();
        return milestone != null ? milestone.buzzChanceBonus : 0f;
    }

    /// <summary>
    /// 現在の広告費割引率を取得する（0〜1、例: 0.1 = 10%OFF）。
    /// マイルストーン未達成の場合は 0 を返す。
    /// </summary>
    public float GetAdDiscountRate()
    {
        var milestone = GetCurrentMilestone();
        return milestone != null ? milestone.adDiscountRate : 0f;
    }

    /// <summary>
    /// 全マイルストーンのリストを取得する（UI表示用）。
    /// </summary>
    public IReadOnlyList<FollowerMilestoneData> GetAllMilestones()
    {
        return _milestones;
    }

    /// <summary>
    /// 指定フォロワー数でのマイルストーン達成状況を確認する（デバッグ用）。
    /// </summary>
    public void LogMilestoneStatus()
    {
        int followers = _statusModel.Followers.Value;
        var current = GetCurrentMilestone();
        var next = GetNextMilestone();

        Debug.Log($"[FollowerSystem] フォロワー: {followers}" +
                  $" | 現在マイルストーン: {(current != null ? current.requiredFollowers.ToString() : "なし")}" +
                  $" | 次のマイルストーン: {(next != null ? next.requiredFollowers.ToString() : "全達成")}");
    }
}

