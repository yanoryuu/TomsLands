using System.Collections.Generic;
using System.Linq;

/// <summary>
/// ダンジョンSOのフェーズ構成から、実際に戦うウェーブ列を組み立てる。
/// GameConst.battleTempo.normalWaveRepeat が 2 以上のとき、ボスを含まない通常フェーズを
/// その回数ぶん繰り返し、ボスを含むフェーズは最後に1回だけ置く。
///
/// ※ 呼ぶのは DungeonWaveSource（実戦闘・クリア見込み・自動プレイが同じウェーブ列を使う）と、BattleContext の Resources フォールバックだけ。
///    配信の dungeonPhases で戦うダンジョン×レベルは展開済みの形なので、ここを通さない。
/// </summary>
public static class BattleWavePlan
{
    /// <summary>GameConst の設定値で展開する。</summary>
    public static List<DungeonPhaseData> Expand(List<DungeonPhaseData> phases)
        => Expand(phases, GameConst.Data?.battleTempo?.normalWaveRepeat ?? 1);

    /// <summary>通常フェーズを repeat 回繰り返し、ボスフェーズを末尾に置いた新しいリストを返す。</summary>
    public static List<DungeonPhaseData> Expand(List<DungeonPhaseData> phases, int repeat)
    {
        if (phases == null) return new List<DungeonPhaseData>();
        if (repeat <= 1) return phases;

        var normal = phases.Where(p => !ContainsBoss(p)).ToList();
        var boss = phases.Where(ContainsBoss).ToList();

        var result = new List<DungeonPhaseData>(normal.Count * repeat + boss.Count);
        for (int i = 0; i < repeat; i++)
            result.AddRange(normal);
        result.AddRange(boss);
        return result;
    }

    private static bool ContainsBoss(DungeonPhaseData phase)
        => phase?.enemies != null && phase.enemies.Any(e => e != null && e.isBoss);
}
