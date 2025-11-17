using UnityEngine;
using R3;

public class CharacterView : MonoBehaviour
{
    [Header("コンポーネント参照")]
    [SerializeField] private SpriteRenderer characterSpriteRenderer;
    [SerializeField] private CharacterStatusView statusView; // ★ CharacterStatusViewへの参照を持つ

    // このViewがクリックされたことをPresenterに通知するための仕組み
    public Subject<Unit> OnClicked { get; } = new Subject<Unit>();

    /// <summary>
    /// Presenterからの指示で初期設定を行う
    /// </summary>
    public void Initialize(CharacterPresenter presenter, string characterName, Sprite sprite)
    {
        gameObject.name = characterName;
        if (sprite != null)
        {
            characterSpriteRenderer.sprite = sprite;
        }

        // ★ CharacterStatusViewにも初期化を指示
        //    Presenterを渡して、UIのイベント購読などを設定させる
        statusView.Initialize(presenter);
    }

    /// <summary>
    /// Presenterからの指示でダメージエフェクトの再生を命令する
    /// </summary>
    public void PlayDamageEffect(int damageAmount)
    {
        // ★ CharacterStatusViewが持つエフェクト再生処理を呼び出す
        statusView.PlayDamageEffects(damageAmount);
    }

    // マウスクリックを検知してPresenterに通知する
    private void OnMouseDown()
    {
        OnClicked.OnNext(Unit.Default);
    }
}