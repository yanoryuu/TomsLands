using UnityEngine;
using UnityEngine.EventSystems;
using Utage;

/// <summary>
/// 会話 UI（AdvUguiManager の GameObject）上のクリックを宴の文字送りへ渡す。ScenarioPlayer が自動で付ける。
///
/// 背景: 宴はクリックを「画面全体の背景レイ」（InputManager の UguiBackgroundRaycaster、優先度 -1 で最背面）で受ける。
/// だが会話 Canvas（Overlay / sortingOrder 2000）には全面の DarkOverlay とメッセージウィンドウの Image が
/// raycastTarget で置かれており、クリックは必ずそちらに当たって背景レイまで届かない。
/// その親を辿っても PointerDown の受け手が無いため、クリックが捨てられていた。
/// ここで親として受け、宴の本来の受け口（AdvUguiManager.OnPointerDown）へ流す。
/// ボタン（選択肢・メニュー）は自身で PointerDown を処理するので、ここまで上がってこない（二重に反応しない）。
/// </summary>
[RequireComponent(typeof(AdvUguiManager))]
public sealed class UtageClickForwarder : MonoBehaviour, IPointerDownHandler
{
    private AdvUguiManager manager;

    private void Awake() => manager = GetComponent<AdvUguiManager>();

    public void OnPointerDown(PointerEventData eventData)
    {
        // 左クリック以外は AdvUguiManager 側で弾かれる
        if (manager != null) manager.OnPointerDown(eventData);
    }
}
