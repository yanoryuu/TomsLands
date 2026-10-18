# 現代的 C#（C# 9 まで）

Unity 6000.0.32f1 は **C# 9**。コンパイルが通らない構文を書かない。

## 使える / 使えない

| 機能 | C# | 可否 |
|---|---|---|
| switch 式、プロパティ/リレーショナル/`and`/`or`/`not` パターン | 8–9 | 可 |
| `??=`、`using var`（宣言）、static ローカル関数 | 8 | 可 |
| target-typed `new()` | 9 | 可（`private readonly List<int> list = new();` は既存コードの標準） |
| `record`（参照型）、`init` アクセサ | 9 | 可（ただしセーブ/JsonUtility 対象には使わない。下記） |
| `record struct`、`readonly record struct` | 10 | **不可** |
| file-scoped namespace、global using、constant interpolated string | 10 | **不可** |
| `required`、raw string literal `"""`、list pattern | 11 | **不可** |
| primary constructor（クラス）、collection expression `[1, 2]` | 12 | **不可** |

名前空間はそもそも付けない運用。新規ファイルでも付けない。

## 式形式

```csharp
// Before
public bool IsFull { get { return pinned.Count >= Capacity; } }
// After（実コードの ScrapbookModel と同じ形）
public bool IsFull => pinned.Count >= Capacity;

public static bool CanPinKind(NewsIssueEntry entry) =>
    entry != null && entry.kind == NewsEntryKind.Report;
```

3 行を超えて読みにくくなるなら通常のメソッド本体に戻す。

## null 合体・null 条件

```csharp
name ??= "none";                         // 純 C# 参照型ならOK
var text = data?.Title ?? "(無題)";      // data が純 C# クラスならOK
```

### UnityEngine.Object には `?.` / `??` / `??=` を使わない

`Destroy` 済みの `GameObject` / `Component` / `ScriptableObject` は `== null` が true になる「偽 null」で、`?.` と `??` はこれを **バイパスして** 破棄済みオブジェクトにアクセスする。

```csharp
// NG: 破棄済みでも weapon?.Fire() が呼ばれる / ?? が右辺に行かない
weapon?.Fire();
var t = cachedTransform ?? transform;
button ??= GetComponent<Button>();

// OK
if (weapon != null) weapon.Fire();
var t = cachedTransform ? cachedTransform : transform;
if (!button) button = GetComponent<Button>();
```

対象: `GameObject`, `Component` 系（`MonoBehaviour`, `Transform`, `Button`, `TextMeshProUGUI` ...）, `ScriptableObject`, `Sprite`, `Texture`, `Material`, `AudioClip`。
`View` クラス（MonoBehaviour）も該当する。迷う型は `UnityEngine.Object` を継承しているか確認する。
元スキルは `GetComponentInChildren<Weapon>()?.Fire()` を推奨していたが **不採用**。

## パターンマッチ

```csharp
// is パターン
if (entry is { kind: NewsEntryKind.Report }) { ... }   // entry が純 C# クラスなら可
if (obj is not null) { ... }          // 純 C# のみ。UnityEngine.Object には使わない（偽 null を見逃す）

// switch 式
string label = state switch
{
    ScrapState.Pending => "砂時計",
    ScrapState.Unconfirmed => "?",
    ScrapState.ConfirmedHit => "○",
    ScrapState.ConfirmedMiss => "×",
    _ => throw new ArgumentOutOfRangeException(nameof(state)),   // 新規コードの「到達したらバグ」は throw
};
```

- 既存の `switch` 文を式に直すとき、**default 分岐の挙動（例外 or フォールバック値）を変えない**
- enum の `switch` に分岐を足しても enum の値・順序は変えない（§0-4）

## record の扱い

```csharp
public record Quote(string ItemId, int Price);   // 可: 実行時だけの不変データ
```

- `JsonUtility` はプロパティ/`init` を保存しない。**セーブ対象は従来どおり `[Serializable] class` + public フィールド**（`ScrapbookModel.Scrap` が実例）
- 既存の `struct`/`class` を `record` に置き換えるのは挙動（等価性、コピー）が変わるのでリファクタリングではやらない
- `record struct` は不可（C# 10）

## その他

- `var` は右辺で型が明らかなときだけ（`var list = new List<int>();` は可、`var x = GetThing();` は不可）
- 文字列補間 `$""` は可。ただしホットパスでは毎フレーム作らない
- ローカル関数は可。ラムダのキャプチャによるアロケーションに注意（`static` ローカル関数にできるなら付ける）
- `Span<T>` / `stackalloc` は必要な局所最適化のときだけ（一般コードに持ち込まない）

## チェックリスト

- [ ] C# 10 以降の構文を書いていない
- [ ] `UnityEngine.Object` に `?.` `??` `??=` `is null` を使っていない
- [ ] record をセーブ対象にしていない
- [ ] 式形式への書き換えで挙動（例外・フォールバック）が変わっていない
