# R3 によるイベントと状態

TomsLands は **R3**（`using R3;`）。UniRx / SignalBus / MessagePipe / Zenject / TheOne.* は使わない。元スキルの `signalbus-events.md` / `theone-framework.md` の SignalBus 前提は全て R3 に置き換える。

| 目的 | 道具 |
|---|---|
| 「ボタンが押された」等の一回きりのイベント | `Subject<T>`（View が持ち、`Observable<T>` で公開） |
| 値が変わる状態（所持金、ターン、フェーズ） | `ReactiveProperty<T>`（Model が持つ） |
| グローバルなイベントバス | 作らない。必要なら Model の `Subject` か `ReactiveProperty` |

## View: Subject を Observable として公開

実コード（`NewspaperView`）の形:

```csharp
public class NewspaperView : MonoBehaviour
{
    [SerializeField] private Button closeButton;

    private readonly Subject<Unit> onCloseClicked = new();
    private readonly Subject<string> onArticleClicked = new();

    public Observable<Unit> OnCloseClicked => onCloseClicked;
    public Observable<string> OnArticleClicked => onArticleClicked;

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => onCloseClicked.OnNext(Unit.Default));
    }

    private void OnDestroy()
    {
        onCloseClicked.Dispose();
        onArticleClicked.Dispose();
    }
}
```

- 公開型は `Observable<T>`（`Subject<T>` を公開しない）。ただし既存の `public Subject<Unit> OnPauseToggled { get; } = new();` のような公開形は **§0-10 に従って変えない**
- 値のないイベントは `Unit`（`Unit.Default`）
- `Subject` は `OnDestroy` で `Dispose()`。View は **ロジックを持たない**（押された事実を流すだけ）
- ボタン参照は null-safe に（`if (button != null)`）。未配線でも起動できる慣習

## Model: ReactiveProperty で状態を公開

```csharp
public class TomsModel
{
    public ReactiveProperty<int> PlayerMoney { get; } = new(0);
}
// 購読側
tomsModel.PlayerMoney.Subscribe(view.ShowMoney).AddTo(disposables);
// 現在値だけ欲しいとき
int money = tomsModel.PlayerMoney.Value;
```

- 外部からの `Value` 書き込みは Model のメソッド経由に寄せる（[data-models](data-models.md)）
- 購読した瞬間に現在値が 1 回流れる（`ReactiveProperty`）。初期描画と二重にならないか確認する
- 既存の `Value` 代入が 1 箇所でも Save と対になっていたら、その対を崩さない（§0-9）

## Presenter: 購読は必ず解放

```csharp
private readonly CompositeDisposable disposables = new();

public void Start()
{
    view.OnCloseClicked.Subscribe(_ => Close()).AddTo(disposables);
    view.OnArticleClicked.Subscribe(OpenArticle).AddTo(disposables);
    stateManager.CurrentTomsShopPhase
        .Subscribe(p => { if (p != TomsShopGamePhase.Newspaper) returnPhase = p; })
        .AddTo(disposables);
}

public void Dispose() => disposables.Dispose();
```

| 購読する側 | 解放 |
|---|---|
| Presenter（IDisposable） | `.AddTo(disposables)` と `Dispose()` |
| MonoBehaviour | `.AddTo(this)`（破棄時に自動解除） |
| どちらでもない短命なもの | `using` / 手動 `Dispose` |

`AddTo` を付けない `Subscribe` は **購読リーク（🔴）**。例外は寿命がアプリ全体と同じ Singleton が自分自身のイベントを購読する場合だけ。

## 注意

- 購読コールバックの中で、同じ Subject に `OnNext` する再入に注意（無限ループ）
- 順序依存: `Subscribe` を呼んだ順に通知される。購読の追加・並べ替えで挙動が変わる（§0-8）。`Entry` の登録順は動かさない
- `Subscribe` のラムダが `this` をキャプチャしても、`AddTo` で解除されれば問題ない
- 毎フレーム通知する Observable（`Observable.EveryUpdate`、頻繁な `ReactiveProperty`）に重い処理を載せない
- R3 の演算子は UniRx と名前が違う場合がある（`Observable<T>` は `IObservable<T>` ではない、`AsObservable()` / `Observable.Merge` など）。UniRx の記法をコピペしない
- 非同期との橋渡しは `FirstAsync(ct)` / `ToUniTask` 等を使えるが、既存コードに合わせる（[unitask-patterns](unitask-patterns.md)）
- `Subject` に `OnCompleted` を流すと以後購読できなくなる。通常は `Dispose` のみ

## 元スキルから置換した概念

| 元（TheOne） | TomsLands |
|---|---|
| `SignalBus.Fire<T>()` / `Subscribe<T>` | `Subject<T>.OnNext` / `Observable<T>.Subscribe` |
| `IInitializable` + `ITickable` + `IDisposable` の登録 | `IStartable` + `IDisposable`（EntryPoint） |
| `ReactiveProperty`（UniRx） | `ReactiveProperty`（R3） |
| `IDisposable.AddTo(this)` | `AddTo(this)` / `AddTo(disposables)` |
