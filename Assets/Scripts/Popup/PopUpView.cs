using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VContainer;

/// <summary>
/// PopupScene 内の Canvas にアタッチするビュー。
/// シーンがロードされた時点で、VContainer 経由で注入された PopUpManager からデータを取り出して UI に反映する。
///
/// ■ データ受信の仕組み:
///   PopUpManager.CurrentData は Show() の時点で既にセット済み。
///   Additive ロード完了後に本クラスの Start() が走るため、
///   確実にデータが存在する状態で読み取れる。
///   PopUpManager は PopupLifetimeScope が親 LifetimeScope から解決して注入する。
/// </summary>
public class PopUpView : MonoBehaviour
{
    [SerializeField] private List<Popup> popups = new List<Popup>();
    
    
    private PopUpManager popUpManager;

    /// <summary>
    /// VContainer による依存注入。
    /// PopupLifetimeScope 経由で親コンテナの PopUpManager が注入される。
    /// </summary>
    [Inject]
    public void Construct(PopUpManager manager)
    {
        this.popUpManager = manager;
    }

    private void Start()
    {
        if (popUpManager == null)
        {
            Debug.LogError("[PopUpView] PopUpManager が注入されていません。PopupLifetimeScope の設定を確認してください。");
            return;
        }

        PopUpData data = popUpManager.CurrentData;

        if (data == null)
        {
            Debug.LogError("[PopUpView] PopUpData が設定されていません。PopUpManager.Show() を経由せずにシーンが読み込まれた可能性があります。");
            return;
        }

        foreach (var popup in popups)
        {
            if (popup.popupSize == data.Size)
            {
                popup.SetData(data);
                break;
            }
        }
        
        
    }
}

