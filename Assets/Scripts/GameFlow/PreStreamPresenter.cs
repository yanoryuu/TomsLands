using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// 配信前の寄り道（鍛冶屋で仕入れてから配信へ）を受け持つプレゼンター。
///
/// 背景: 通常営業では陳列分が全部売れる（売り注文制）ため、配信日の朝には在庫が空になりやすい。
/// 配信の品出し設定(StreamingSetting)は FightScene 側にあり鍛冶屋(TomsShop側)へ直接行けないので、
/// TomsShop を離れる直前（GameFlowManager が配信日に入った時点）で一旦止め、
/// 「鍛冶屋へ寄る / このまま配信へ」を選ばせる。
///
/// 村の広場が Lv5 なら、その前に「勝負配信の宣言」（1ランに1回。StreamDeclaration）を持ちかける。
/// 順番は 宣言 → 今日は配信日（鍛冶屋へ寄る / このまま配信へ）。勝負の融資（銀行 Lv5）は前日の朝に出し済み（DebtPresenter）で、
/// 借りていれば「今日は配信日」の本文に返す日と額を出す。宣言も同じ本文に出す。
///
/// 画面は既存の BlackSmith とポップアップ(PopUpManager)だけを使うので、シーン/プレハブの配線は不要。
/// 鍛冶屋が使えない（未登録）・ポップアップが無い場合は従来どおり即配信へ進む。
/// </summary>
public class PreStreamPresenter : IStartable, IDisposable
{
    private const string PopupSceneName = "PopupScene";

    private readonly GameFlowManager gameFlowManager;
    private readonly StateManager stateManager;
    private readonly PopUpManager popUpManager;
    private readonly ItemModel itemModel;
    private readonly DungeonRepository dungeonRepository;
    private readonly TomsModel tomsModel;
    private readonly CancellationTokenSource cts = new();

    /// <summary>宣言を持ちかけた配信（GameFlow インデックス）。同じ配信の前に二度出さない。</summary>
    private int declarationOfferedIndex = -1;

    public PreStreamPresenter(
        GameFlowManager gameFlowManager,
        StateManager stateManager,
        PopUpManager popUpManager,
        ItemModel itemModel,
        DungeonRepository dungeonRepository,
        TomsModel tomsModel = null)
    {
        this.gameFlowManager = gameFlowManager;
        this.stateManager = stateManager;
        this.popUpManager = popUpManager;
        this.itemModel = itemModel;
        this.dungeonRepository = dungeonRepository;
        this.tomsModel = tomsModel;
    }

    public void Start()
    {
        // NextTurn は利用者操作（サマリーの確認）からしか呼ばれないので、Start での登録で間に合う
        gameFlowManager.SetPreStreamHandler(OnPreStream);
    }

    /// <param name="fromProcurement">true=鍛冶屋を閉じたところ / false=配信日に入った直後</param>
    private void OnPreStream(bool fromProcurement)
    {
        bool canVisitBlackSmith = stateManager.HasHandler(TomsShopGamePhase.BlackSmith);
        if (popUpManager == null || !canVisitBlackSmith)
        {
            gameFlowManager.ProceedToBattle();
            return;
        }

        // 村の広場 Lv5: 配信日に入った直後、まず勝つ側の宣言を持ちかける（使わなければ何も起きない）
        if (!fromProcurement && declarationOfferedIndex != gameFlowManager.CurrentIndex && StreamDeclaration.CanOffer(tomsModel))
        {
            declarationOfferedIndex = gameFlowManager.CurrentIndex;
            ShowDeclarationOffer();
            return;
        }

        ShowStreamDecision(fromProcurement);
    }

    private void ShowStreamDecision(bool fromProcurement)
    {
        int kinds = 0, total = 0;
        if (itemModel?.RuntimeItems != null)
        {
            var stocked = itemModel.RuntimeItems.Where(r => r != null && r.Stock.Value > 0).ToList();
            kinds = stocked.Count;
            total = stocked.Sum(r => r.Stock.Value);
        }

        string dungeonName = PendingDungeonName();

        string stockLine = total > 0
            ? $"配信に持ち込める在庫: {kinds}種 {total}個"
            : "配信に持ち込める在庫がありません！";

        // 最後の配信なら、終わったら決算だと先に知らせる（K-11）
        bool final = gameFlowManager.IsUpcomingStreamFinal();
        string finalLine = final ? "\nこれが最後の配信。終わったら決算で、在庫は今の相場で評価されます。" : "";
        // 村 Lv5 の勝負（宣言・融資）をしていれば、何に張っているかを同じ本文に出す
        string betLines = BuildVillageBetLines();

        if (!fromProcurement)
        {
            string where = string.IsNullOrEmpty(dungeonName) ? "" : $"（{dungeonName}）";
            popUpManager.Show(new PopUpData
            {
                Title = final ? $"今日は最後の配信{where}" : $"今日は配信日{where}",
                Message = $"{stockLine}{betLines}{finalLine}\n配信の前に鍛冶屋で仕入れますか？",
                ConfirmButtonText = "鍛冶屋へ寄る",
                CancelButtonText = "このまま配信へ",
                Size = PopupSizeEnum.Medium,
                OnConfirm = GoToBlackSmith,
                OnCancel = gameFlowManager.ProceedToBattle,
            });
        }
        else
        {
            popUpManager.Show(new PopUpData
            {
                Title = final ? "最後の配信を始めますか？" : "配信を始めますか？",
                Message = stockLine + betLines + finalLine,
                ConfirmButtonText = "配信を始める",
                CancelButtonText = "仕入れを続ける",
                Size = PopupSizeEnum.Medium,
                OnConfirm = gameFlowManager.ProceedToBattle,
                OnCancel = null, // 鍛冶屋に留まる
            });
        }
    }

    private string PendingDungeonName()
    {
        var key = gameFlowManager.PendingBattleDungeon;
        return key.HasValue && dungeonRepository != null ? dungeonRepository.GetById(key.Value)?.dungeonName : null;
    }

    /// <summary>「宣言: 勇者が勝つ」「融資: 770,000G を 5日目に返す」の行（無ければ空）。</summary>
    private string BuildVillageBetLines()
    {
        if (tomsModel == null) return "";
        var sb = new System.Text.StringBuilder();
        var side = StreamDeclaration.ActiveFor(tomsModel, gameFlowManager.CurrentIndex);
        if (side != StreamDeclarationSide.None)
            sb.Append($"\n宣言: {StreamDeclaration.Label(side)}");
        if (BankLoan.HasOutstanding(tomsModel))
        {
            string when = gameFlowManager.IsUpcomingStreamFinal() ? "決算で差し引き" : $"{DayText.DayOf(tomsModel.BankLoanDueTurn)}に返す";
            sb.Append($"\n融資: {tomsModel.BankLoanOwed:N0}G を{when}");
        }
        return sb.ToString();
    }

    // ========================================
    // 村の広場 Lv5「勝負配信の宣言」
    // ========================================

    /// <summary>宣言するか → どちらが勝つか → 確認、の順に出す。どこで「使わない」を選んでも「今日は配信日」へ進む。</summary>
    private void ShowDeclarationOffer()
    {
        string dungeonName = PendingDungeonName();
        string where = string.IsNullOrEmpty(dungeonName) ? "" : $"（{dungeonName}）";
        popUpManager.Show(new PopUpData
        {
            Title = StreamDeclaration.OfferTitle,
            Message = $"今日の配信{where}の勝ち側を宣言しますか？（1ランに1回）\n{StreamDeclaration.EffectLines()}",
            ConfirmButtonText = StreamDeclaration.UseButton,
            CancelButtonText = StreamDeclaration.SkipButton,
            Size = PopupSizeEnum.Medium,
            OnConfirm = () => ShowNextAsync(ShowDeclarationSideChoice).Forget(),
            OnCancel = () => ShowNextAsync(() => ShowStreamDecision(false)).Forget(),
        });
    }

    private void ShowDeclarationSideChoice()
    {
        popUpManager.Show(new PopUpData
        {
            Title = StreamDeclaration.ChooseSideTitle,
            Message = "勝つと思う側を選んでください。",
            ConfirmButtonText = StreamDeclaration.HeroButton,
            CancelButtonText = StreamDeclaration.DemonButton,
            Size = PopupSizeEnum.Medium,
            OnConfirm = () => ShowNextAsync(() => ShowDeclarationConfirm(StreamDeclarationSide.Hero)).Forget(),
            OnCancel = () => ShowNextAsync(() => ShowDeclarationConfirm(StreamDeclarationSide.Demon)).Forget(),
        });
    }

    /// <summary>最後の確認（B/Esc はポップアップのキャンセルに当たるので、選んだ側をここで見せ直して取り違えを防ぐ）。</summary>
    private void ShowDeclarationConfirm(StreamDeclarationSide side)
    {
        int flowIndex = gameFlowManager.CurrentIndex;
        popUpManager.Show(new PopUpData
        {
            Title = $"「{StreamDeclaration.Label(side)}」と宣言する",
            Message = StreamDeclaration.EffectLines(),
            ConfirmButtonText = StreamDeclaration.UseButton,
            CancelButtonText = StreamDeclaration.CancelButton,
            Size = PopupSizeEnum.Medium,
            OnConfirm = () =>
            {
                StreamDeclaration.Declare(tomsModel, side, flowIndex);
                ShowNextAsync(() => ShowStreamDecision(false)).Forget();
            },
            OnCancel = () => ShowNextAsync(() => ShowStreamDecision(false)).Forget(),
        });
    }

    /// <summary>
    /// 前のポップアップが閉じきってから次を出す（PopUpManager は確定の処理のあとで閉じるので、処理の中で続けて出すと閉じる側に巻き込まれる）。
    /// 待っている間に配信へ進んでいたら出さない。
    /// </summary>
    private async UniTaskVoid ShowNextAsync(Action show)
    {
        try
        {
            await UniTask.WaitUntil(() => popUpManager.CurrentData == null && !SceneManager.GetSceneByName(PopupSceneName).isLoaded,
                cancellationToken: cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!gameFlowManager.IsAwaitingStream.Value) return;
        show();
    }

    private void GoToBlackSmith()
    {
        // ポップアップ表示中に別経路で配信へ進んでいたら何もしない
        if (!gameFlowManager.IsAwaitingStream.Value) return;
        stateManager.ChangeTomsShopPhase(TomsShopGamePhase.BlackSmith);
        Debug.Log("[PreStream] 配信前に鍛冶屋へ寄り道");
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
        gameFlowManager?.SetPreStreamHandler(null);
    }
}
