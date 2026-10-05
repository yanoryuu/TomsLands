using R3;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 店キャラ立ち絵の表情をバズ状態に応じて差し替える小コンポーネント。
/// 立ち絵の Image に付け、通常/バズ/超バズ/炎上のスプライトを割り当てる。
/// 未設定の段階は「通常」へフォールバックする（超バズ→バズ→通常、炎上→通常）。
/// BuzzSystem は所属 View の [Inject] から Bind() で渡す（未バインドなら何もしない）。
/// </summary>
public class CharacterExpression : MonoBehaviour
{
    [SerializeField] private Image target;
    [SerializeField] private Sprite normal;
    [SerializeField] private Sprite buzz;
    [SerializeField] private Sprite bigBuzz;
    [SerializeField] private Sprite flame;

    private CompositeDisposable _disposables;
    private BuzzSystem _buzz;
    private bool _initialized;

    private void Awake()
    {
        if (target == null) target = GetComponent<Image>();
    }

    public void Bind(BuzzSystem buzzSystem)
    {
        _disposables?.Dispose();
        _disposables = null;
        _buzz = buzzSystem;
        _initialized = false;
        if (_buzz == null) return;

        _disposables = new CompositeDisposable();
        _buzz.IsBuzzActive
            .CombineLatest(_buzz.CurrentBuzzType, (active, type) => (active, type))
            .Subscribe(s => Apply(s.active, s.type))
            .AddTo(_disposables);
    }

    private void OnEnable()
    {
        // 非表示中に変わった状態を、表示した時点で反映する（演出なし）
        if (_buzz != null) Apply(_buzz.IsBuzzActive.Value, _buzz.CurrentBuzzType.Value, false);
    }

    private void OnDestroy()
    {
        _disposables?.Dispose();
    }

    private void Apply(bool active, BuzzType type, bool animate = true)
    {
        if (target == null) return;

        Sprite sprite = normal;
        if (active)
        {
            sprite = type switch
            {
                BuzzType.Big => bigBuzz != null ? bigBuzz : buzz,
                BuzzType.Flame => flame,
                _ => buzz
            };
            if (sprite == null) sprite = normal;
        }
        if (sprite == null || target.sprite == sprite) { _initialized = true; return; }

        target.sprite = sprite;
        // 初回反映と非表示中の変化はポップさせない
        if (animate && _initialized && isActiveAndEnabled)
            UIFx.Pop(target.transform, active && type == BuzzType.Big ? 0.9f : 0.95f, 0.22f);
        _initialized = true;
    }
}
