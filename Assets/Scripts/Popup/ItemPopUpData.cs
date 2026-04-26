using System;
using UnityEngine;

/// <summary>
/// アイテム市場分析ポップアップに渡す型付きデータ。
/// BlackSmithPresenter 等が生成して ItemPopUpManager.Show() に渡す。
/// </summary>
public class ItemPopUpData
{
    // ---- 基本情報 ----
    public string ItemName;
    public string Description;
    public Sprite Icon;
    public ItemTypeData.ItemType ItemType;
    public ItemTypeData.ItemAttribute ItemAttribute;

    // ---- 需要 ----
    public float Demand;            // 0–1
    public float PreviousDemand;    // 0–1
    
    // ---- 価格 ----
    public int CurrentPrice;
    public int BasePrice;
    public int PreviousPrice;

    // ---- 在庫 ----
    public int Stock;
    public int MaxStock;

    // ---- 売れやすさ・販売実績 ----
    public float SalesRate;
    public bool WasSoldLastTurn;
    public bool IsPopular;

    // ---- ボタン ----
    public string ConfirmButtonText;
    public Action OnConfirm;
}

