
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using R3;

public class InfoBrokerView : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform messageParent;
    [SerializeField] private GameObject messageSlotPrefab;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button refreshButton;
    [SerializeField] private TextMeshProUGUI messageText;

    public Subject<Unit> OnCloseRequested { get; } = new();
    public Subject<Unit> OnRefreshRequested { get; } = new();

    private readonly List<GameObject> activeMessageSlots = new();

    // カジュアルな挨拶メッセージ
    private readonly List<string> greetingMessages = new()
    {
        "よう！何か聞きたいことあるかい？",
        "やあ、今日も情報収集してるよ",
        "おつかれさん！面白い話があるんだ",
        "こんにちは！勇者の動向、気になる？",
        "よう！最近の勇者の様子、教えてやろうか？",
        "お疲れ様！今日も色々見てきたよ"
    };

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
        refreshButton.onClick.AddListener(() => OnRefreshRequested.OnNext(Unit.Default));

        ShowRandomGreeting();
    }

    private void ShowRandomGreeting()
    {
        var random = new System.Random();
        var greeting = greetingMessages[random.Next(greetingMessages.Count)];
        messageText.text = greeting;
    }

    // public void DisplayInfoMessages(List<InfoMessage> messages)
    // {
    //     // 既存メッセージをクリア
    //     foreach (var slot in activeMessageSlots)
    //     {
    //         Destroy(slot);
    //     }
    //     activeMessageSlots.Clear();
    //
    //     if (messages.Count == 0)
    //     {
    //         // メッセージがない場合のデフォルトメッセージ
    //         var defaultSlot = Instantiate(messageSlotPrefab, messageParent);
    //         var defaultSlotComponent = defaultSlot.GetComponent<InfoMessageSlot>();
    //         var defaultMessage = new InfoMessage("特に変わったことはないかな。また後で来てよ。", InfoType.General, 0.5f);
    //         defaultSlotComponent.SetMessage(defaultMessage);
    //         activeMessageSlots.Add(defaultSlot);
    //         return;
    //     }
    //
    //     foreach (var message in messages)
    //     {
    //         var slotObj = Instantiate(messageSlotPrefab, messageParent);
    //         var slot = slotObj.GetComponent<InfoMessageSlot>();
    //         slot.SetMessage(message);
    //         activeMessageSlots.Add(slotObj);
    //     }
    // }
    /// <summary>
    /// モデルから渡された InfoMessage の一覧を
    /// messageText にまとめて表示する
    /// </summary>
    public void DisplayInfoMessages(List<InfoMessage> messages)
    {
        // null ガード
        if (messages == null || messages.Count == 0)
        {
            // メッセージがない場合のデフォルト表示
            messageText.text = "特に変わったことはないかな。また後で来てよ。";
            return;
        }

        // InfoMessage の本文を改行でつなげて一つのテキストにする
        var lines = messages
            .Select(m => m.message)
            .ToList();

        messageText.text = string.Join("\n", lines);
    }
}