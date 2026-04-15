using UnityEngine;
using VContainer;

/// <summary>
/// ItemPopupScene 内の Canvas にアタッチするビュー。
/// シーンがロードされた時点で、VContainer 経由で注入された ItemPopUpManager からデータを取り出して UI に反映する。
///
/// ■ データ受信の仕組み:
///   ItemPopUpManager.CurrentData は Show() の時点で既にセット済み。
///   Additive ロード完了後に本クラスの Start() が走るため、
///   確実にデータが存在する状態で読み取れる。
///   ItemPopUpManager は ItemPopupLifetimeScope が親 LifetimeScope から解決して注入する。
/// </summary>
public class ItemPopUpView : MonoBehaviour
{
    [SerializeField] private ItemPopup itemPopup;

    private ItemPopUpManager itemPopUpManager;

    /// <summary>
    /// VContainer による依存注入。
    /// ItemPopupLifetimeScope 経由で親コンテナの ItemPopUpManager が注入される。
    /// </summary>
    [Inject]
    public void Construct(ItemPopUpManager manager)
    {
        this.itemPopUpManager = manager;
    }

    private void Start()
    {
        if (itemPopUpManager == null)
        {
            Debug.LogError("[ItemPopUpView] ItemPopUpManager が注入されていません。ItemPopupLifetimeScope の設定を確認してください。");
            return;
        }

        ItemPopUpData data = itemPopUpManager.CurrentData;

        if (data == null)
        {
            Debug.LogError("[ItemPopUpView] ItemPopUpData が設定されていません。ItemPopUpManager.Show() を経由せずにシーンが読み込まれた可能性があります。");
            return;
        }

        if (itemPopup != null)
        {
            itemPopup.SetData(data);
        }
        else
        {
            Debug.LogError("[ItemPopUpView] itemPopup が Inspector で未設定です！");
        }
    }
}

