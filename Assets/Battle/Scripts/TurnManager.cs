using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class TurnOrderView : MonoBehaviour
{
    [SerializeField] private GameObject iconPrefab;
    [SerializeField] private Transform iconContainer;

    private BattleManager _battleManager;

    void Start()
    {
        _battleManager = FindObjectOfType<BattleManager>();
        if (_battleManager != null)
        {
            _battleManager.OnTurnOrderChanged += UpdateView;
        }
    }

    void OnDestroy()
    {
        if (_battleManager != null)
        {
            _battleManager.OnTurnOrderChanged -= UpdateView;
        }
    }

    private void UpdateView(List<BattleCharacter> characters)
    {
        // 既存のアイコンを一旦すべて削除
        foreach (Transform child in iconContainer)
        {
            Destroy(child.gameObject);
        }

        if (characters == null || iconPrefab == null) return;

        // 新しいリストに基づいてアイコンを生成
        foreach (var character in characters)
        {
            var iconGo = Instantiate(iconPrefab, iconContainer);
            var iconImage = iconGo.GetComponent<Image>();
            
            // キャラクターのスプライトをアイコンに設定
            if (iconImage != null && character.CharacterSprite != null)
            {
                iconImage.sprite = character.CharacterSprite;
            }
        }
    }
}