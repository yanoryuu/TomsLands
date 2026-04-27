using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// バトル中に客キャラクターを管理する。
/// Inspector に登録した CustomerCharacter GameObjects をプールとして使い回す。
/// StreamingSalesController から RequestCustomer() を呼ぶことで購入演出を開始する。
/// </summary>
public class CustomerManager : MonoBehaviour
{
    [Header("客キャラクター")]
    [Tooltip("シーン上に配置・非表示にしておく CustomerCharacter GameObjects。ランダムに選ばれる。")]
    [SerializeField] private List<CustomerCharacter> customerCharacters;

    [Header("スポーン位置")]
    [Tooltip("左側の画面外スポーン位置")]
    [SerializeField] private Transform spawnPointLeft;
    [Tooltip("右側の画面外スポーン位置")]
    [SerializeField] private Transform spawnPointRight;

    [Header("売り場前の停車位置")]
    [Tooltip("売り場スロットの前に置く停車ポイント（スロット数分設定推奨）")]
    [SerializeField] private List<Transform> destinationPoints;

    [Header("生成設定")]
    [Tooltip("複数購入が連続した時の客同士の出現間隔（秒）")]
    [SerializeField] private float spawnInterval = 0.4f;
    [Tooltip("同時に画面上に存在できる客の最大数（0 = 無制限）")]
    [SerializeField] private int maxConcurrentCustomers = 5;

    private readonly Queue<RuntimeItemData> _purchaseQueue = new Queue<RuntimeItemData>();
    private bool _isProcessing;
    private int _destIndex;

    private void Awake()
    {
        // 全キャラを非表示にして待機状態にする
        if (customerCharacters != null)
            foreach (var c in customerCharacters)
                if (c != null) c.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────
    //  公開 API
    // ─────────────────────────────────────────

    /// <summary>
    /// アイテムが売れた時に StreamingSalesController から呼ぶ。
    /// キューに積んで順次処理する。
    /// </summary>
    public void RequestCustomer(RuntimeItemData item)
    {
        if (customerCharacters == null || customerCharacters.Count == 0) return;
        if (destinationPoints == null || destinationPoints.Count == 0) return;

        // 同時上限チェック（アクティブ数で判定）
        int activeCount = customerCharacters.Count(c => c != null && c.gameObject.activeSelf);
        if (maxConcurrentCustomers > 0 && activeCount >= maxConcurrentCustomers) return;

        _purchaseQueue.Enqueue(item);
        if (!_isProcessing) ProcessQueueAsync().Forget();
    }

    // ─────────────────────────────────────────
    //  内部処理
    // ─────────────────────────────────────────

    private async UniTaskVoid ProcessQueueAsync()
    {
        _isProcessing = true;
        while (_purchaseQueue.Count > 0)
        {
            SpawnCustomer(_purchaseQueue.Dequeue());
            if (_purchaseQueue.Count > 0)
                await UniTask.Delay(TimeSpan.FromSeconds(spawnInterval));
        }
        _isProcessing = false;
    }

    private void SpawnCustomer(RuntimeItemData item)
    {
        // 非アクティブなキャラをランダム順で探す
        var available = customerCharacters
            .Where(c => c != null && !c.gameObject.activeSelf)
            .ToList();
        if (available.Count == 0) return;

        var customer = available[UnityEngine.Random.Range(0, available.Count)];

        // 停車位置をラウンドロビンで選択
        Transform dest = destinationPoints[_destIndex % destinationPoints.Count];
        _destIndex++;

        // 左右ランダムに出現
        bool fromLeft = UnityEngine.Random.value > 0.5f;
        Vector3 spawnPos = fromLeft
            ? (spawnPointLeft  != null ? spawnPointLeft.position  : new Vector3(-12f, 0f, 0f))
            : (spawnPointRight != null ? spawnPointRight.position : new Vector3( 12f, 0f, 0f));

        customer.gameObject.SetActive(true);
        customer.Play(item.ItemIcon, spawnPos, dest.position);
    }
}
