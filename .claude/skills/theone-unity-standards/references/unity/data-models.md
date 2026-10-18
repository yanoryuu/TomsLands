# データは Model 経由（元 Data Controller の置換）

元スキルの「Data Controller / Data Manager」は、TomsLands では **Model**（Singleton 登録の純 C# クラス）が担う。`ItemModel`, `TomsModel`, `NewsModel`, `ScrapbookModel`, `NewspaperSubscriptionModel` など。

## 原則

1. Model が状態の **唯一の持ち主**。Presenter / View が Model の内部コレクションを直接書き換えない
2. 状態の変更は Model のメソッドで行い、**変更後に Save**（Presenter が呼ぶのが既存の型）
3. 読み取りは `IReadOnlyList<T>` や `ReactiveProperty<T>` で公開する
4. Model は MonoBehaviour にしない。`Lifetime.Singleton` で登録する

## 実例（ScrapbookModel）

```csharp
public class ScrapbookModel
{
    private readonly List<Scrap> pinned = new();   // 内部は private
    public IReadOnlyList<Scrap> Pinned => pinned;  // 外には読み取り専用

    public bool IsFull => pinned.Count >= Capacity;

    public bool Pin(NewsIssueEntry entry, int turn)   // 変更はメソッド経由、結果は戻り値
    {
        if (!CanPin(entry)) return false;
        pinned.Add(new Scrap { entryKey = entry.Key, pinnedTurn = turn });
        return true;
    }
}
```

Presenter 側（変更 → Save）:

```csharp
private void PinArticle(string entryKey)
{
    var entry = newsModel.FindEntry(entryKey);
    if (!scrapbook.Pin(entry, Turn)) return;
    Save();            // 状態を変えたら必ず保存（§0-9）
    RenderScraps();
}

private void Save() =>
    NewspaperSaveStore.Save(tomsModel.FlowSeed, subscriptions, scrapbook, newsModel);
```

## 悪い例と直し方

```csharp
// NG: Presenter が Model の内部リストを直接触る
scrapbook.Pinned.Add(...);                  // IReadOnlyList なので本来コンパイルが通らない設計にする
tomsModel.PlayerMoney.Value -= price;       // 不整合チェック・Save が抜けやすい

// OK: Model にメソッドを用意（新規コード）
public bool TrySpend(int price)
{
    if (PlayerMoney.Value < price) return false;
    PlayerMoney.Value -= price;
    return true;
}
```

リファクタリングで既存の直接書き換えを Model メソッドに寄せる場合は、**呼び出し元ごと挙動が同じ**ときだけ（検証・Save の有無・通知回数が変わらないこと）。迷ったら報告。

## セーブ

- 保存形式は `JsonUtility` + `[Serializable] class`（`NewspaperSaveStore.SaveData`）。**フィールド名・型・既定値は変えない**（§0-3）
- 保存先は `SaveSlotManager.GetPath(FileName)`。ファイル名は文字列キーなので変えない（§0-6）
- 保存・読み込みは try/catch して `Debug.LogWarning`、失敗しても起動を止めない
- ロードはシード（`FlowSeed`）が変わった周でのみ行う（`EnsureLoaded`）。周回リセットは `RunSaveCleaner` に登録する
- 真偽のような再現できる値は保存せず、シードから再構築する設計がある（ScrapbookModel の注記）。「保存していない」ものを勝手に保存対象に増やさない

## 状態の公開

| 欲しいもの | 公開形 |
|---|---|
| 変化を購読したい値 | `ReactiveProperty<T>`（`Value` で現在値） |
| 一覧 | `IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>` |
| 派生値 | 式形式プロパティ `public bool IsFull => ...;` |
| 画面内だけの選択状態 | Model に置かず Presenter のフィールド（`selectedPage` など。保存しない） |

## レビューの観点

- [ ] 状態変更は Model のメソッド経由、変更後に Save が残っている
- [ ] Model の内部コレクションが外に露出していない
- [ ] 保存クラスのフィールド名・型・既定値が不変
- [ ] 新規 Model は Singleton 登録の純 C#（`GameLifetimeScope` に登録）
- [ ] 画面一時状態を Model に混ぜていない
