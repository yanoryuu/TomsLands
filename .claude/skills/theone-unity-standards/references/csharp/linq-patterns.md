# LINQ パターン

可読性が上がる箇所で使ってよい。ただし **ホットパスでは使わない**。

## ホットパス（LINQ 禁止）

- `Update` / `LateUpdate` / `FixedUpdate` と、そこから毎フレーム呼ばれるメソッド
- 戦闘ティック、自動プレイ、Market の ABM など大量ループ
- 毎フレーム発火する R3 の購読（`Observable.EveryUpdate` の中、頻繁に更新される `ReactiveProperty`）
- 数千件規模のループ

理由: `Where/Select/ToList` は列挙子・デリゲート・コレクションをヒープに確保し、IL2CPP の Android では GC スパイクの原因になる。
判断に迷ったら「1 秒に何回呼ばれるか」を見る。画面表示時やボタン押下時に 1 回なら LINQ でよい。

## 基本の置き換え

```csharp
// 非ホットパス（朝刊の描画、Entry など）: LINQ でよい
var unread = entries.Where(e => !e.isRead).OrderBy(e => e.publishTurn).ToList();

// ホットパス: foreach + 事前確保したバッファ
private readonly List<Enemy> alive = new();
private void CollectAlive()
{
    alive.Clear();
    foreach (var e in enemies)
        if (e.IsAlive) alive.Add(e);
}
```

## List<T> の既存メソッドで済むものは LINQ を使わない

実コード（NewspaperPresenter）は `itemModel.RuntimeItems.Exists(i => ...)` を使っている。`List<T>` には `Exists / Find / FindAll / FindIndex / RemoveAll / ForEach / Sort` があり、LINQ より軽い。

| やりたいこと | 推奨 |
|---|---|
| 存在確認 | `list.Exists(x => ...)`（`Any` より軽い） |
| 最初の 1 件 | `list.Find(x => ...)` / ループ |
| 件数 | `list.Count`（`Count()` ではなく） |
| 末尾 | `list[list.Count - 1]` |
| 条件削除 | `list.RemoveAll(x => ...)` |

## よくある落とし穴

```csharp
// 遅延評価: 列挙のたびに再評価される。複数回使うなら ToList() で実体化
var q = items.Where(i => i.Stock.Value > 0);   // まだ実行されない
foreach (var i in q) { }                       // ここで実行
var n = q.Count();                             // もう一度実行

// ループ内 LINQ（O(n^2)）
foreach (var a in articles)
{
    var company = companies.First(c => c.id == a.companyId);   // NG → Dictionary を先に作る
}
```

- `Select` の中で副作用を起こさない
- `First()` は空で例外、`FirstOrDefault()` は null/default を返す。既存の挙動（例外か null か）を変えない
- `Dictionary` / `HashSet` が適する検索は LINQ ではなくそれらで（`openedBodies` は `HashSet<string>`）
- `Where(o => o != null)` は Unity の `==` が使われるので偽 null も除外できるが、ラムダ内で `o?.x` は書かない

## LINQ とループを相互に直すときの注意

- `foreach` 中にコレクションを変更しない（`InvalidOperationException`）。先に `ToList()` で複製していた箇所は、その複製を残す
- ソートの安定性: `OrderBy` は安定、`List.Sort` は **不安定**。同値の並び順が変わると挙動が変わるので、`OrderBy` を `Sort` に置き換えない（§0）
- 乱数やシードに依存する列挙順は絶対に変えない。ニュースのカレンダーはシードから再現されるので、順序が変わると過去セーブと食い違う

## チェックリスト

- [ ] ホットパスに LINQ がない
- [ ] `List<T>` のメソッドで済む箇所に LINQ を使っていない
- [ ] 遅延評価の多重実行がない
- [ ] LINQ とループの置き換えで順序・例外・安定性が変わっていない
