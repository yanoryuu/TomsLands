using UnityEngine;
using TMPro;
using Cysharp.Threading.Tasks;

/// <summary>
/// ダメージ数値を上へフェードアウトさせながら表示し、完了後に自身を破棄します。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class DamagePopup : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private TMP_Text damageText;

    [Header("アニメーション設定")]
    [SerializeField] private const float moveDistance = 50f;
    [SerializeField] private const float duration = 0.5f;

    private Vector3 startLocalPos;
    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        startLocalPos = transform.localPosition;
    }

    /// <summary>
    /// ダメージ表示アニメーションを実行します。
    /// このメソッドが完了すると、このゲームオブジェクトは破棄されます。
    /// </summary>
    public async UniTask ShowAsync(int damage)
    {
        if (damageText == null || canvasGroup == null) return;

        // アニメーションの初期設定
        gameObject.SetActive(true);
        transform.localPosition = startLocalPos;
        damageText.text = $"-{damage}";
        canvasGroup.alpha = 1f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 進捗率に応じて、位置と透明度を更新
            transform.localPosition = startLocalPos + Vector3.up * moveDistance * t;
            canvasGroup.alpha = 1f - t;

            await UniTask.Yield(this.GetCancellationTokenOnDestroy());
        }
        
        // アニメーションが完了したら、自分自身のゲームオブジェクトを破棄します
        Destroy(gameObject);
    }
}
