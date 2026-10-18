# VContainer（DI）

TomsLands は VContainer 1.17。Zenject / TheOne.DI は無い。登録は各シーンの `*LifetimeScope`（`GameLifetimeScope`, `BattleLifetimeScope`, `EventLifetimeScope`, `BootLifetimeScope`）に集約。

## 基本ルール

| 対象 | 登録 | 注入 |
|---|---|---|
| Model（純 C#） | `builder.Register<XxxModel>(Lifetime.Singleton);` | コンストラクタ |
| Presenter | `builder.RegisterEntryPoint<XxxPresenter>();` | コンストラクタ |
| シーン上の View（MonoBehaviour） | `RegisterComponentSafe(builder, xxxView, nameof(xxxView));` | Presenter のコンストラクタ |
| 既存 MonoBehaviour が DI を使う | LifetimeScope 側の自動注入 | `[Inject] public void Construct(...)` |

実例（`GameLifetimeScope`）:

```csharp
builder.Register<ItemModel>(Lifetime.Singleton);
builder.Register<NewspaperSubscriptionModel>(Lifetime.Singleton);

RegisterComponentSafe(builder, blackSmithView, nameof(blackSmithView));
if (newspaperView != null) builder.RegisterEntryPoint<NewspaperPresenter>();

private static void RegisterComponentSafe<T>(IContainerBuilder builder, T component, string fieldName) where T : class
{
    if (component == null)
    {
        Debug.LogError($"[GameLifetimeScope] {fieldName} が Inspector で未設定です！");
        return;
    }
    builder.RegisterComponent(component);
}
```

- View が未設定ならその Presenter は登録しない（`if (view != null) RegisterEntryPoint<...>()`）。これが「未配線でも起動できる」慣習
- 新しい View を足すときは `[SerializeField] private XxxView xxxView;` を LifetimeScope に足し、`RegisterComponentSafe` で登録する。**既存の SerializeField 名は変えない**（§0-1）

## Presenter（EntryPoint）の形

```csharp
public class FooPresenter : IStartable, IDisposable
{
    private readonly FooView view;
    private readonly FooModel model;
    private readonly CompositeDisposable disposables = new();

    public FooPresenter(FooView view, FooModel model)
    {
        this.view = view;
        this.model = model;
        // フェーズ入口の登録はコンストラクタ（StateManager.RegisterOnEnter）。順序依存なので動かさない
    }

    public void Start()
    {
        view.OnCloseClicked.Subscribe(_ => Close()).AddTo(disposables);
        model.Count.Subscribe(view.ShowCount).AddTo(disposables);
    }

    public void Dispose() => disposables.Dispose();
}
```

- 依存は **コンストラクタ注入**。`IStartable.Start` と `Dispose` を実装
- コンストラクタでは重い処理や購読を増やさない。購読は `Start` へ（既存でコンストラクタ購読になっているものは順序依存なので移さない。§0-8）
- 登録順・購読順・`IStartable.Start` の呼び出し順に依存するクラスがある（`StateManager` / `GameLifecycleHandler` / `TomsShopPresenter.Entry`）。登録行の並べ替えは禁止

## [Inject] と [Preserve]

```csharp
// MonoBehaviour への注入（実例: BattleControlView）
[Inject]
public void Construct(PopUpManager popUpManager)
{
    this.popUpManager = popUpManager;
}
```

- 新規 MonoBehaviour は `[Inject]` メソッドで受ける。フィールド/プロパティ注入より **メソッド注入**
- IL2CPP のコード削減（stripping）で DI 対象のコンストラクタが消えるのを防ぐため、`[Preserve]`（`UnityEngine.Scripting`）は **新規クラスでは付けてよい（任意）**。既存クラスへ一括で付けて回ることはしない（差分が広がるだけで、現状 link.xml / ビルドで動いている）
- ビルドでだけ注入に失敗したら、まず Player.log を見て stripping を疑う

## 禁止・注意

- `FindObjectOfType` / `GameObject.Find` / `FindFirstObjectByType` を新規に書かない。View は LifetimeScope の SerializeField 経由で渡す（既存は報告のみ）
- Model を `MonoBehaviour` にしない。Singleton の純 C# クラスにする
- `Lifetime.Singleton` の Model が Presenter より後に登録されていても VContainer は解決できるが、**登録順に意味があるもの**（コンストラクタ購読の順序など）は触らない
- 共有 ScriptableObject はシーン直参照せず Addressables 経由で取得して登録する（`BattleLifetimeScope` が `AddressableLoader.Load<BattleInputData>("SceneData/BattleInputData")` で取得）。ビルドで SO が複製されて別インスタンスになる不具合の回避
- `Resolve` を Presenter の中で呼ぶ Service Locator 的な使い方をしない。必要な依存はコンストラクタに出す

## レビューの観点

- [ ] 新規 Presenter は `IStartable, IDisposable`、`CompositeDisposable` を持つ
- [ ] View 未設定時に落ちない（`RegisterComponentSafe` / null ガード）
- [ ] 登録行を並べ替えていない、既存の SerializeField 名を変えていない
- [ ] コンストラクタ注入、新規 MonoBehaviour は `[Inject]` メソッド
- [ ] `FindObjectOfType` を新規に書いていない
