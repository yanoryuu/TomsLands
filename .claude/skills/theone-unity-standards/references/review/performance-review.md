# パフォーマンスレビュー

対象は Android IL2CPP。GC スパイクと毎フレームのアロケーションが主な敵。元の `performance-review.md` と `performance-optimizations.md` を統合。**最適化は挙動を変えないこと（§0）が前提**。計測せず推測だけで大きく書き換えない。

## 1. ホットパスの定義

- `Update` / `LateUpdate` / `FixedUpdate`
- 戦闘ティック、自動プレイ、Market ABM、毎フレーム発火する R3 ストリーム
- Tween の `OnUpdate` コールバック

ここでは次を禁止する:

| NG | 代替 |
|---|---|
| `new List<T>()` / `new T[]` | フィールドに事前確保して `Clear()` で再利用 |
| LINQ（`Where/Select/ToList/Any`） | `foreach` + バッファ、`List.Exists` |
| 文字列連結・補間 `$"..."` | 値が変わったときだけ更新、`StringBuilder` を再利用 |
| ラムダのキャプチャ | `static` ラムダ、メソッド参照をフィールドにキャッシュ |
| `GetComponent` / `Find` / `Camera.main` | Awake でキャッシュ |
| boxing（`object` への値型代入、`enum.ToString()`、非ジェネリック Dictionary） | ジェネリックコレクション、`switch` で文字列化 |
| `foreach` の `IEnumerable<T>` 経由列挙（インターフェースにボックス化） | `List<T>` の型で `foreach` するか `for` |

## 2. 具体例

```csharp
// Before: 毎フレーム List とテキストを作る
void Update()
{
    var alive = enemies.Where(e => e.IsAlive).ToList();
    hpText.text = $"HP {alive.Count}";
}

// After
private readonly List<Enemy> alive = new();
private int lastAliveCount = -1;

void Update()
{
    alive.Clear();
    foreach (var e in enemies) if (e.IsAlive) alive.Add(e);
    if (alive.Count == lastAliveCount) return;
    lastAliveCount = alive.Count;
    hpText.text = $"HP {alive.Count}";     // 変化したときだけ
}
```

```csharp
// 距離比較は sqrMagnitude（平方根を避ける）。閾値を二乗にして比較し、結果は同じ
if ((a - b).sqrMagnitude < range * range) { ... }
```

- `CompareTag("Enemy")`（`tag ==` は文字列を確保する）
- `Dictionary<K,V>` / `HashSet<T>` で O(1) 検索。ループ内の `List.Contains` / `First` は O(n²)
- 辞書キーに enum を使うなら `EqualityComparer` を指定するか int キーに（Mono のボックス化回避）
- 配列やリストは容量が分かるなら `new List<T>(capacity)`
- `string` の連結ループは `StringBuilder`
- 構造体のコピーが大きいときは `in` / `ref`（読み取り専用の大きな struct）

## 3. R3・UniTask・DOTween 関連

- `Observable.EveryUpdate().Subscribe` を大量に作らない。1 本にまとめて配る
- 購読の `Subscribe` を `Update` や毎フレーム通知の中で増やさない（リーク）
- `.Subscribe(x => ...)` のラムダは購読時に 1 回確保される。ホットな箇所では OK だが、毎フレーム購読し直さない
- `UniTask.Delay` ループで毎フレーム待つ代わりに、`UniTask.WaitUntil` / `Observable` を使う
- DOTween の `new Sequence` を毎フレーム作らない。`SetLink` で破棄漏れを防ぐ。`DOTween.Kill` 忘れで Tween が溜まる

## 4. メモリ・Addressables

- ロードしたアセット/ハンドルは使い終わったら解放（`AddressableLoader` の対になる解放）
- `Resources.Load` を新規に増やさない
- `Texture2D` / `Sprite` を `new` したら破棄（`Destroy`）
- 大きなテクスチャを UI で小さく使わない（圧縮設定・サイズはアセット側の責務。コード変更では触らない）

## 5. ログ

- ホットパスの `Debug.Log` は削除してよい（一時デバッグ用のみ。進行ログと `LogError/LogWarning` は残す。[quality-hygiene](../csharp/quality-hygiene.md)）
- `Debug.Log($"...")` は文字列補間のコストを毎回払う。頻度の高い箇所は出さない

## 6. 触ってはいけない最適化

次は速くなりそうでも **振る舞いが変わる** ため、リファクタとしてやらない。見つけたら報告:

- 実行順序（Awake/Start、購読順、`IStartable.Start`）を変える「遅延初期化」
- ソートを `OrderBy` → `List.Sort`（不安定ソートで同値の順序が変わる）
- 乱数の呼び出し回数・順序の変更（シード再現がずれる）
- `float` の演算順序の変更（結果が微妙に変わる）
- キャッシュ導入で Save/Load・フェーズ遷移後に古い値が残る

## 7. 重大度

- 🔴 Update 内の GetComponent/Find/Camera.main、毎フレームの `new`/LINQ/文字列補間、購読リーク、ボックス化の大量発生
- 🟡 非ホットパスの LINQ、`tag ==`、ループ内の線形検索、Debug.Log の乱発
- 🟢 `sqrMagnitude`、`StringBuilder`、容量指定

## チェックリスト

- [ ] ホットパスにアロケーション源がない
- [ ] キャッシュ導入が状態更新と矛盾しない（無効化タイミングがある）
- [ ] 最適化で実行順序・乱数順序・ソート順序が変わっていない
- [ ] 計測できない推測だけの大改造をしていない
