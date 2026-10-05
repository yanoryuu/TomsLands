using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

/// <summary>
/// 見た目確認用: InterventionCardView にダミーのスパチャ・魔物・ボタン状態を流す（エディタ専用）。
/// プレハブには付けない。確認用シーンの任意のオブジェクトに付けて右クリックメニューから使う。
/// </summary>
public class InterventionCardDebugFeeder : MonoBehaviour
{
#if UNITY_EDITOR
    [SerializeField] private InterventionCardView card;
    [SerializeField] private SpecialMoveCutInView cutIn;
    [SerializeField] private Sprite[] enemyIcons;
    [SerializeField] private bool autoFeed = true;
    [SerializeField] private float superChatInterval = 1.6f;

    private float _next;
    private int _seq;
    private int _specialLeft = 2;
    private int _bossLeft = 1;

    private static readonly string[] Messages =
    {
        "がんばれ！", "その剣どこで買える？", "", "ボス倒して！", "うおおおお", "初見です", "回復して…", "神回",
    };

    private void Start()
    {
        if (card == null) card = FindAnyObjectByType<InterventionCardView>();
        if (cutIn == null) cutIn = FindAnyObjectByType<SpecialMoveCutInView>();
        if (card == null) return;
        ApplyButtons();
        FeedEnemies();
        for (int i = 0; i < 4; i++) PushRandom();
        card.OnHeroRequested.Subscribe(t =>
        {
            Debug.Log($"[DebugFeeder] Hero {t}");
            if (t == HeroInterventionType.Special) _specialLeft = Mathf.Max(0, _specialLeft - 1);
            card.PushSuperChat(new SuperChatInfo(
                t == HeroInterventionType.Heal ? SuperChatColor.Green : t == HeroInterventionType.Skill ? SuperChatColor.Blue : SuperChatColor.Red,
                t == HeroInterventionType.Heal ? 10000 : t == HeroInterventionType.Skill ? 50000 : 100000, "", true));
            ApplyButtons();
            SimulatePendingThenCooldown().Forget();
            if (t == HeroInterventionType.Special && cutIn != null) cutIn.PlayAsync(destroyCancellationToken).Forget();
        }).AddTo(this);
        card.OnDungeonRequested.Subscribe(t =>
        {
            Debug.Log($"[DebugFeeder] Dungeon {t}");
            if (t == DungeonInterventionType.BossBuff) _bossLeft = 0;
            ApplyButtons();
            SimulatePendingThenCooldown().Forget();
        }).AddTo(this);
    }

    private async UniTaskVoid SimulatePendingThenCooldown()
    {
        var ct = destroyCancellationToken;
        card.SetPending(true);
        await UniTask.Delay(1500, true, cancellationToken: ct);
        card.SetPending(false);
        float t = 0f, dur = 6f;
        while (t < dur)
        {
            card.SetCooldown(1f - t / dur);
            await UniTask.Yield(ct);
            t += Time.unscaledDeltaTime;
        }
        card.SetCooldown(0f);
    }

    private void Update()
    {
        if (!autoFeed || card == null) return;
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + superChatInterval;
        PushRandom();
        if (Random.value < 0.35f) FeedEnemies();
    }

    [ContextMenu("Push random SuperChat")]
    private void PushRandom()
    {
        if (card == null) return;
        var c = (SuperChatColor)Random.Range(0, 7);
        int amount = c switch
        {
            SuperChatColor.Blue => 200, SuperChatColor.Cyan => 500, SuperChatColor.Green => 1000,
            SuperChatColor.Yellow => 2000, SuperChatColor.Orange => 5000, SuperChatColor.Magenta => 10000, _ => 50000,
        };
        card.PushSuperChat(new SuperChatInfo(c, amount, Messages[_seq++ % Messages.Length], false));
    }

    [ContextMenu("Feed enemies")]
    private void FeedEnemies()
    {
        if (card == null) return;
        var list = new List<EnemyBadgeInfo>();
        int n = Random.Range(2, 5);
        for (int i = 0; i < n; i++)
        {
            Sprite s = enemyIcons != null && enemyIcons.Length > 0 ? enemyIcons[i % enemyIcons.Length] : null;
            list.Add(new EnemyBadgeInfo(s, Random.Range(0.1f, 1f), i == n - 1 && Random.value < 0.6f));
        }
        card.SetEnemies(list);
    }

    [ContextMenu("Flip")]
    private void DoFlip() => card?.Flip();

    [ContextMenu("Toggle affordable (poor)")]
    private void Poor()
    {
        if (card == null) return;
        card.SetHeroButton(HeroInterventionType.Heal, new InterventionButtonState(10000, true, -1));
        card.SetHeroButton(HeroInterventionType.Skill, new InterventionButtonState(50000, false, -1));
        card.SetHeroButton(HeroInterventionType.Special, new InterventionButtonState(100000, false, _specialLeft));
    }

    private void ApplyButtons()
    {
        card.SetHeroButton(HeroInterventionType.Heal, new InterventionButtonState(10000, true, -1));
        card.SetHeroButton(HeroInterventionType.Skill, new InterventionButtonState(50000, true, -1));
        card.SetHeroButton(HeroInterventionType.Special, new InterventionButtonState(100000, true, _specialLeft));
        card.SetDungeonButton(DungeonInterventionType.Trap, new InterventionButtonState(10000, true, -1));
        card.SetDungeonButton(DungeonInterventionType.Curse, new InterventionButtonState(30000, true, -1));
        card.SetDungeonButton(DungeonInterventionType.Reinforce, new InterventionButtonState(50000, true, -1));
        card.SetDungeonButton(DungeonInterventionType.BossBuff, new InterventionButtonState(100000, true, _bossLeft));
    }
#endif
}
