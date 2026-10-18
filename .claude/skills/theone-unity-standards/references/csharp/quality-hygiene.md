# 品質・衛生（TomsLands 版）

SKILL.md §1 の詳細。**§0（互換）に触れる変更は衛生目的でも行わない**。迷ったら [refactoring-safety](../review/refactoring-safety.md)。

元スキルからの方針変更:
- `#nullable enable` の一括導入は **しない**（既存コード全体に警告が出て、`[SerializeField]` の未設定参照とも相性が悪い）
- 「インラインコメント禁止」は **不採用**（日本語の「なぜ」コメントは資産）
- 「エラーは必ず throw」は **不採用**（未配線でも起動できる null-safe が慣習）
- `TheOne.Logging` は無い。Unity の `Debug` を使う

## 1. アクセス修飾子・readonly・const・nameof

```csharp
// Before
class Foo { int count; List<int> list = new(); public int Max = 10; }

// After（Max は Inspector から見えるなら SerializeField/public のまま。§0-1）
public class Foo
{
    private const int MaxRetry = 3;
    private readonly List<int> list = new();
    private int count;
}
```

- `private` も省略しない（既存ファイルを触るときに足す。無関係なファイルの一括変更はしない）
- `public` を `private` に下げる前に確認すること: UnityEvent（`m_MethodName`）、`SendMessage`/`Invoke`、Editor/Debug コード、リフレクション、他ディレクトリからの参照
- 再代入しないフィールドは `readonly`。ただし `[SerializeField]` と `[Serializable]` のフィールドには付けない
- 引数名・メンバー名の文字列は `nameof`（`RegisterComponentSafe(builder, view, nameof(view))` が実例）。ただし **セーブキー・Animator パラメータ名・Addressables アドレスは nameof にしない**（リネームで値が変わる）

## 2. 警告ゼロ・デッドコード

消してよいもの:
- 未使用 `using`、未使用ローカル変数、未使用 `private` メンバー、到達不能コード
- コメントアウトされたコード（履歴は git にある）
- 空の `Update()` / `Start()` / `FixedUpdate()`（空の Unity メッセージは呼び出しコストがある）。**中身があるものの移動・削除は §0-7**

消してはいけないもの:
- `#pragma warning disable` を足して警告を黙らせるのは禁止（原因を直す）
- リフレクション/UnityEvent/Animation Event から呼ばれる private っぽいメソッド（未使用に見えて使われている）。削除前に grep（[refactoring-safety](../review/refactoring-safety.md)）

## 3. コメント方針

```csharp
// 残す: なぜ・前提・順序の意味・仕様書参照
// TomsModel.CurrentTurn はラン終了時にしか同期されないので、進行中のターンは GameFlowManager から取る
private int Turn => gameFlowManager != null ? gameFlowManager.CurrentTurn.Value : tomsModel.CurrentTurn.Value;

// 消す: コードの言い換え
hp -= damage;   // HPを減らす   <- 削除

// 直す: 嘘になったコメント
```

- `///` の XML ドキュメント（`<summary>`）は日本語で残す。仕様書参照（`Docs/News_Spec.md §9`）は特に残す
- 「なぜ」が必要なのに名前が悪いときは、コメントを足す前にメソッド名・変数名を直す（private のみ）

## 4. エラー処理

| 状況 | やること |
|---|---|
| 既存の `Debug.LogError` / `LogWarning` + 継続 | **そのまま**（throw 化は挙動変更） |
| 新規コード: 起きたらバグ（内部不整合、switch の到達しない分岐） | `throw new InvalidOperationException(...)` / `ArgumentException` |
| 新規コード: データ欠損・Inspector 未配線 | `Debug.LogWarning` / `LogError` + 安全に継続（`GameLifetimeScope.RegisterComponentSafe` の形） |
| 外部 I/O（セーブ読み書き、Addressables） | try/catch して `Debug.LogWarning`（`NewspaperSaveStore` の形） |

```csharp
// 禁止: 握り潰し
try { Load(); } catch { }

// 可
try { Load(); }
catch (Exception e) { Debug.LogException(e); }
```

- 例外を握ったら必ずログに出す。`catch (Exception)` で変数を捨てない
- 非同期の例外は `UniTaskVoid` の中で try/catch する（[unitask-patterns](../unity/unitask-patterns.md)）

## 5. ログ

- TheOne.Logging は無い。`Debug.Log/LogWarning/LogError/LogException` を使う
- デバッグメニューの「ログ」タブが `LogError`/`LogWarning`/`LogException` を読むので **削除しない**
- `[Battle]` `[GameLifetimeScope]` のようなクラス名プレフィックスは検索用。残す・新規でも付ける
- 一時デバッグ用（毎フレーム出る、値ダンプだけ）の `Debug.Log` は削除してよい。意味のある進行ログは残す
- 文字列補間でログ文字列を毎フレーム組み立てない（ホットパスでは出さないか、出力頻度を絞る）

## 6. マジックナンバー・長いメソッド・重複

- 調整値は `GameConst` / `GameConstSettings` / `NewsTuning` のような既存の定数置き場へ。**値は変えない**
- 80 行を超えるメソッドは意味のまとまりで private メソッドに抽出（同一クラス内）。抽出で **呼び出し順・購読順・Save のタイミングが変わらない** ことを確認する
- 重複は同一ファイル/同一ディレクトリ内で共通化。ディレクトリ横断は報告にとどめる

```csharp
// 抽出例: Presenter.Entry が長いとき、順序をそのまま private メソッドへ
private void Entry()
{
    BuildCalendar();
    RestoreSelection();
    RenderAll();
}
```

## 7. 命名

- private フィールド: camelCase（コンストラクタで `this.view = view;` と代入するのが既存の型）。`_` プレフィックスは既存ファイルに合わせる
- public メンバー・メソッド: PascalCase
- bool は `Is/Has/Can` 始まり
- **MonoBehaviour / ScriptableObject のクラス名・ファイル名は変えない**（§0-2）

## チェックリスト

- [ ] §0 に触れていない
- [ ] 修飾子明示、readonly/const/nameof（シリアライズ対象・文字列キーを除く）
- [ ] 未使用 using・変数・メンバー・コメントアウトコード削除
- [ ] 言い換えコメント削除、「なぜ」コメント保持
- [ ] 空 catch なし、既存の LogError は残っている
