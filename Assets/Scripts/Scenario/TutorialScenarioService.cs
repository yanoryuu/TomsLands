using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 会話の再生と既読管理をまとめた薄いサービス。
/// TomsShop 以外のシーン（FightScene 等）からも同じ規則で会話を出せるようにするために切り出している。
/// 既読フラグは MetaProgressModel（slot_N/metaData.json）に持つ。
/// </summary>
public class TutorialScenarioService
{
    private readonly MetaProgressModel _meta;

    public TutorialScenarioService(MetaProgressModel meta)
    {
        _meta = meta;
    }

    /// <summary>指定の会話を再生済みか。</summary>
    public bool HasSeen(string label) => _meta != null && _meta.HasSeenTutorial(label);

    /// <summary>
    /// 未読なら一度だけ再生し、既読にする。既読・ラベル未定義なら何もしない。
    /// </summary>
    public async UniTask PlayOnceAsync(string label, CancellationToken cancellationToken = default)
    {
        if (_meta == null || _meta.HasSeenTutorial(label)) return;
        if (!await PlayAsync(label, cancellationToken)) return;
        _meta.MarkTutorialSeen(label);
    }

    /// <summary>
    /// 既読に関係なく毎回再生する。ランダムイベントの会話のように繰り返すもの向け。
    /// </summary>
    public static async UniTask PlayAlwaysAsync(string label, CancellationToken cancellationToken = default)
    {
        await PlayAsync(label, cancellationToken);
    }

    /// <summary>
    /// 実際の再生。ScenarioPlayer が居ない、またはラベルが存在しない場合は false を返して何もしない。
    /// </summary>
    private static async UniTask<bool> PlayAsync(string label, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(label)) return false;

        var player = UnityEngine.Object.FindFirstObjectByType<ScenarioPlayer>();
        if (player == null)
        {
            Debug.LogWarning($"[TutorialScenarioService] ScenarioPlayer が見つからないため '{label}' をスキップしました。");
            return false;
        }
        // シナリオデータの読み込み前だとラベル判定が常に失敗するため、先に完了を待つ
        await player.WaitUntilReadyAsync(cancellationToken);
        if (!player.HasLabel(label)) return false;

        await player.PlayAsync(label, cancellationToken);
        return true;
    }
}
