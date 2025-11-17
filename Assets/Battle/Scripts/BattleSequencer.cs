using UnityEngine;
using Cysharp.Threading.Tasks;
using R3;
using System.Collections.Generic;
using System.Threading;

public class BattleSequencer : MonoBehaviour
{
    [Header("戦闘系のマネージャー")]
    [SerializeField] private CharacterFactory characterFactory;
    [SerializeField] private BattleUIView battleUIView;

    [Header("戦闘ルール設定")]
    [Tooltip("この戦闘で倒すべき通常モンスターの総数")]
    [SerializeField] private const int totalNormalEnemies = 10;
    [Tooltip("フィールドに同時に出現できる敵の最大数")]
    [SerializeField] private const int maxConcurrentEnemies = 3;

    [Header("ステージデータ")]
    [SerializeField] private DungeonInfoScriptableObj currentDungeon;

    private BattleContext battleContext;

    public Subject<(string weaponId, string armorId)> OnBattleWin { get; } = new();
    public Subject<(string weaponId, string armorId)> OnBattleDefeat { get; } = new();
    public IReadOnlyList<CharacterPresenter> CharacterPresenters =>
        battleContext?.GetAllPresenters() ?? new List<CharacterPresenter>();
    public Subject<(CharacterModel attacker, CharacterModel target)> OnCharacterDamaged { get; } = new();

    // テスト用にStart()で自動開始します
    private void Start()
    {
        Debug.LogWarning("★　テストモードで戦闘を開始");
        var testHeroModel = new HeroModel();
        StartBattle(testHeroModel);
    }

    public void StartBattle(HeroModel heroModel)
    {
        var token = this.GetCancellationTokenOnDestroy();
        BattleStartAsync(heroModel, token).Forget();
    }

    private async UniTaskVoid BattleStartAsync(HeroModel heroModel, CancellationToken token)
    {
        // ★ ここで戦闘ルールをContextに渡す
        battleContext = new BattleContext(currentDungeon, totalNormalEnemies, maxConcurrentEnemies);
        var flowManager = new BattleFlowManager(battleContext, characterFactory, battleUIView, this);

        await flowManager.ExecuteBattleAsync(heroModel, token);
        Debug.Log("戦闘が終了しました。 (BattleSequencer)");
    }
}