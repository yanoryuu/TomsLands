---
name: theone-unity-standards
description: TomsLands（Unity 6 / VContainer + R3 + UniTask + DOTween）の C# コーディング規約とリファクタリング・レビュー基準。TheOne Studio の theone-unity-standards をこのプロジェクト向けに書き換えたもの。Unity C# の実装・リファクタリング・コードレビュー、DI 登録、R3 のイベント購読、Model へのデータアクセス、UniTask の非同期処理を書くときに使う。
---

# TomsLands Unity コーディング規約（theone-unity-standards 改）

元: TheOne Studio `theone-unity-standards`（https://github.com/truongtv22/theone-training-skills）。
TomsLands の実態（VContainer 1.17 + R3 + UniTask + DOTween、名前空間なし、asmdef なし、日本語コメント文化、Android IL2CPP）に合わせて書き換えた。

⚠️ Unity 6000.0.32f1 / **C# 9**。`record struct`、file-scoped namespace、global using、`required` など C# 10 以降の構文は使わない。

## 優先順位

| 優先 | 内容 | 参照 |
|---|---|---|
| **0（絶対）** | **挙動・シリアライズ・セーブ互換を壊さない** | 本ファイル §0, [refactoring-safety](references/review/refactoring-safety.md) |
| 1 | 品質・衛生（アクセス修飾子、readonly/const、nameof、警告ゼロ、デッドコード削除、エラー処理） | [quality-hygiene](references/csharp/quality-hygiene.md) |
| 2 | 現代的 C#（式形式、null 合体、パターンマッチ、LINQ は非ホットパスで） | [modern-csharp](references/csharp/modern-csharp-features.md), [linq](references/csharp/linq-patterns.md) |
| 3 | アーキテクチャ（MVP + VContainer + R3、Model がデータの唯一の持ち主、UniTask） | [vcontainer-di](references/unity/vcontainer-di.md), [r3-events](references/unity/r3-events.md), [models](references/unity/data-models.md), [unitask](references/unity/unitask-patterns.md) |
| 4 | Unity 固有・パフォーマンス | [unity-specifics](references/review/unity-specifics.md), [performance](references/review/performance-review.md) |

## §0 リファクタリングの絶対ルール（違反＝即差し戻し）

リファクタリングは **振る舞いを変えない** 変更だけ。仕様変更・バランス変更・バグ修正（明らかでも）は混ぜず、見つけたら報告する。

シリアライズ/外部参照を壊すので **変更禁止**:
1. `[SerializeField]` フィールド・`public` フィールド・`[Serializable]` クラスのフィールド名と型（シーン/プレハブ/SO の値が消える）。どうしても直すなら `[FormerlySerializedAs]` 必須だが、原則やらない
2. `MonoBehaviour` / `ScriptableObject` のクラス名とファイル名（スクリプト GUID 参照）。ファイル移動もしない
3. JSON セーブ対象（`JsonUtility` / Newtonsoft で保存するクラス）のフィールド名・型・既定値
4. enum のメンバー順序と値（特に `TomsShopGamePhase` は int シリアライズ）。追加は末尾のみ
5. プレハブ/シーンの UnityEvent から呼ばれるメソッド（`m_MethodName`）の名前・public 性・引数。変更前に `grep -r "m_MethodName: 名前" Assets --include=*.prefab --include=*.unity` で確認
6. 文字列キー: Addressables アドレス/ラベル、PlayerPrefs キー、RemoteConfig キー、TSV 列名、Utage のラベル/コマンド名、Animator パラメータ名、`Invoke`/`SendMessage` の文字列
7. Unity メッセージ（`Awake/Start/OnEnable/Update/OnDestroy` …）の追加・削除・実行順の変更。`Awake` と `Start` の間で処理を移動しない
8. VContainer の登録順・`RegisterOnEnter` などコンストラクタ内の購読順・`IStartable.Start` の処理順（StateManager / GameLifecycleHandler / TomsShopPresenter.Entry は順序依存）
9. セーブ呼び出しの削除・移動（「状態変更 → Save」は必ず残す）
10. 公開 API（他ディレクトリから使われる `public` メンバー）のシグネチャ。変える場合は `Assets/` 全体を grep して全呼び出し元を同じ変更内で直す。担当外ファイルに波及するなら変えずに報告
11. `Assets/Scripts` 外（Utage・Plugins・サードパーティ・`Packages/`）のコード
12. リフレクションで名前参照されるメンバー。Debug/ と Editor/AutoPlay は `GetField(obj, "retryButton")` / `InvokeMethod(x, "Close")` のように他画面の **private** フィールド・メソッドを文字列で触る（壊れてもコンパイルは通り、自動プレイが黙って止まる）。private のリネーム・削除前に `grep -rn '"名前"' Assets/Scripts` で確認する

迷ったら変えない。

## §1 品質・衛生

1. **アクセス修飾子は明示**、最小限に。`private` を省略しない。他から使われていない `public` は `private`/`internal` に下げてよい（§0-5 の UnityEvent・§0-1 のシリアライズ対象・Editor/Debug からの参照・反射を確認したうえで）
2. 再代入しないフィールドは `readonly`、コンパイル時定数は `const`、`static readonly` は配列など参照型定数に
3. 引数名・メンバー名を表す文字列は `nameof`
4. **警告ゼロ**: 未使用 using・未使用変数・未使用 private メンバー・到達不能コードを消す。`#pragma warning disable` で黙らせない
5. **デッドコード削除**: コメントアウトされたコード、呼ばれていない private メソッド、空の `Update()`/`Start()`（空の Unity メッセージはコストがあるので消してよい。中身があるものは §0-7）
6. **コメント**（元スキルの「インラインコメント禁止」は不採用）:
   - 日本語で「なぜ」「前提」「順序の意味」「仕様書の参照」を書いたコメントは **資産なので残す**
   - コードをそのまま言い換えただけのコメント（`// HPを減らす` → `hp -= dmg;`）は消して、必要なら名前を良くする
   - 古くなって嘘になったコメントは直す
7. **エラー処理**（元スキルの「必ず throw」は不採用。このプロジェクトは未配線 UI でも起動できる null-safe が慣習）:
   - 既存の `Debug.LogError` → `throw` 置き換えはしない（挙動変更）
   - 新規コードで「起きたらバグ」の箇所は `throw`、データ欠損や未配線は `LogWarning` + 安全に継続
   - 例外を握り潰す `catch {}` は作らない。`catch (Exception e) { Debug.LogException(e); }` を使う
8. **ログ**（TheOne.Logging は無いので Unity の `Debug` を使う）:
   - デバッグメニューの「ログ」タブが読むので、`LogError`/`LogWarning`/`LogException` は残す。`[Battle]` 等の既存プレフィックスも検索用なので残す
   - 一時デバッグ用の `Debug.Log`（毎フレーム出る、値ダンプだけ）は削除してよい。意味のある進行ログは残す
   - 文字列補間を毎フレーム作るログはホットパスに置かない
9. マジックナンバーは `const` か、調整値なら `GameConst` / `GameConstSettings`（値の移動はしても値は変えない）
10. 長すぎるメソッド（目安 80 行超）は、意味のまとまりで private メソッドに抽出する。抽出は同じクラス内に限る
11. 重複コードは同一ファイル/同一ディレクトリ内で共通化する。ディレクトリ横断の共通化は報告にとどめる

## §2 現代的 C#（C# 9 まで）

- 1 行で済むプロパティ・メソッドは式形式 `=>`
- `?? ??= ?.`、`is` パターン、`switch` 式を使う
- **UnityEngine.Object には `?.` / `??` を使わない**（破棄済みオブジェクトの偽 null を見逃す）。`if (obj != null)` か `obj ? a : b`
- `var` は右辺で型が明らかなときに使う
- `new()` の target-typed new は可（C# 9）
- LINQ は可読性が上がる箇所で使う。ただし `Update`/毎フレーム/大量ループ（Market ABM・自動プレイ・戦闘ティック）では使わない
- 文字列連結のループは `StringBuilder`

## §3 アーキテクチャ（TomsLands の型）

スタック: **VContainer（DI） + R3（イベント/状態） + UniTask（非同期） + DOTween（演出）**。SignalBus / MessagePipe / Zenject / UniRx は使わない。

- **MVP**: Model = Singleton 登録の純 C#（状態の唯一の持ち主、`ReactiveProperty` で公開）/ View = MonoBehaviour（`[SerializeField]` と `Subject` を公開、ロジックを持たない）/ Presenter = `IStartable, IDisposable` の EntryPoint
- **データは Model 経由**（元スキルの Data Controller に相当）: Presenter/View が Model の内部コレクションを直接書き換えない。変更は Model のメソッドで行い、変更後に Save
- **購読は必ず解放**: Presenter は `CompositeDisposable disposables` + `.AddTo(disposables)`、`Dispose()` で `disposables.Dispose()`。MonoBehaviour は `.AddTo(this)`
- View が公開するのは `Observable<T>`（`Subject` を `private readonly` で持ち、`public Observable<Unit> OnXxx => xxx;`）。既存の公開形を変えるのは §0-10 に従う
- Subject は Presenter/View の `Dispose`/`OnDestroy` で `Dispose()`
- DI はコンストラクタ注入。新規 MonoBehaviour への注入は `[Inject]` メソッド。`FindObjectOfType` / `GameObject.Find` を新規に書かない（既存は報告）
- `GameLifetimeScope` は `RegisterComponentSafe` パターン。View が null なら Presenter を登録しない
- Addressables は `AddressableLoader.Load/LoadAll` 経由。ロードしたハンドルは解放する

## §4 Unity 固有

- `GetComponent` は Awake でキャッシュ。`Update` 内で `GetComponent`/`Find`/`Camera.main` を呼ばない
- null チェック付き取得は `TryGetComponent`
- DOTween は必ず `.SetLink(gameObject)`。演出は `UI/UIFx.cs` の `PanelOpen/Pop` を再利用
- UniTask: `async void` を新規に書かない（`UniTaskVoid` + `.Forget()`）。MonoBehaviour では `destroyCancellationToken`、Presenter では `CancellationTokenSource` を `Dispose` でキャンセル
- `Update` で毎フレーム `new`（List、文字列補間、ラムダキャプチャ、LINQ）しない
- 初期非アクティブのポップアップで `Awake` 末尾に `SetActive(false)` を書かない（初回 Show が打ち消される）
- Editor 専用コードは `Editor/` 配下か `#if UNITY_EDITOR`

## レビュー/リファクタリング チェックリスト

**§0 互換（最初に必ず）**
- [ ] SerializeField / public / Serializable / セーブ JSON のフィールド名・型が不変
- [ ] MonoBehaviour/SO のクラス名・ファイル名・置き場所が不変
- [ ] enum 順序不変、UnityEvent ターゲットメソッドが不変
- [ ] 文字列キー不変、Unity メッセージと初期化順・購読順・Save 呼び出しが不変
- [ ] 担当外ファイルに波及する公開 API 変更なし

**§1 品質**
- [ ] アクセス修飾子明示 / readonly / const / nameof
- [ ] 未使用 using・変数・メンバー・コメントアウトコード・空 Unity メッセージ削除
- [ ] 言い換えコメント削除、「なぜ」コメントは保持
- [ ] 空 catch なし

**§2〜4**
- [ ] 式形式・パターンマッチ・null 合体（UnityEngine.Object 以外）
- [ ] ホットパスに LINQ / アロケーションなし
- [ ] 購読は AddTo で解放、Subject は Dispose
- [ ] GetComponent キャッシュ、Tween に SetLink、async void なし

## 重大度

- 🔴 必須: §0 違反、購読リーク、空 catch、`UnityEngine.Object` への `?.`、ホットパスの GetComponent/Find
- 🟡 推奨: 修飾子・readonly・const・nameof、デッドコード、長大メソッドの分割、ホットパスの LINQ
- 🟢 任意: 式形式、パターンマッチ、命名改善（private のみ）
