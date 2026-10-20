using System.Collections.Generic;
using VContainer;

/// <summary>
/// 「世界の事実」のうち、プレイヤーがまだ知らないものを管理する。
///
/// 現状はダンジョンの弱点属性のみ。以前は仕入れ画面のバナーが弱点を無条件で表示し、
/// おすすめスコアにも加点していたため、「次に何が売れるか」という<b>未来の情報が
/// 無料で開示されていた</b>。それを塞ぐのがこのモデルの役目。
///
/// 知る手段は3つ（Docs/News_Spec.md §3 の情報源の分担）:
///   ・新聞のダンジョン記事から<b>推論する</b>（無料・確信は持てない）
///   ・情報屋で<b>直接買う</b>（有料・確定）
///   ・村メタ/レリックで恒久解放する
///
/// このモデルが持つのは「情報屋で買ったか」に相当する<b>確定した知識</b>だけ。
/// 記事からの推論はプレイヤーの頭の中で起きることなので、ここには入らない。
/// </summary>
public class DungeonIntelModel
{
    private readonly HashSet<DungeonName> knownWeakness = new();

    // 「情報屋で買ったか」の正（セーブされる DungeonData.isShowedInfo）。null なら knownWeakness だけで判定する
    private readonly DungeonRepository dungeonRepository;

    public DungeonIntelModel() { }

    /// <summary>
    /// 弱点を知っているかは、情報屋で買ったか（isShowedInfo）から導く。
    /// knownWeakness はこの店シーンの間だけのメモで保存されないため、
    /// それだけで判定すると配信やイベントから戻るたび・続きからで弱点が「?」に戻っていた。
    /// </summary>
    [Inject]
    public DungeonIntelModel(DungeonRepository dungeonRepository) => this.dungeonRepository = dungeonRepository;

    /// <summary>全ダンジョンの弱点を常に開示する（村メタ等での恒久解放用）。</summary>
    public bool RevealAll { get; set; }

    public bool IsWeaknessKnown(DungeonName dungeon) =>
        RevealAll || knownWeakness.Contains(dungeon) || IsInfoPurchased(dungeon);

    private bool IsInfoPurchased(DungeonName dungeon) =>
        dungeonRepository != null
        && dungeonRepository.TryGetById(dungeon, out var data)
        && data != null && data.isShowedInfo;

    public void MarkWeaknessKnown(DungeonName dungeon) => knownWeakness.Add(dungeon);

    public void Clear()
    {
        knownWeakness.Clear();
        RevealAll = false;
    }

    // --- セーブ/ロード（周をまたぐ場合はメタ側へ移す） ---

    public List<DungeonName> ToPlain() => new(knownWeakness);

    public void FromPlain(IEnumerable<DungeonName> plain)
    {
        knownWeakness.Clear();
        if (plain == null) return;
        foreach (var d in plain) knownWeakness.Add(d);
    }
}
