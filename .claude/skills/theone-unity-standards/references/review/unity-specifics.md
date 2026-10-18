# Unity 固有のレビュー観点

## 1. コンポーネント取得

```csharp
// Awake でキャッシュ。Update 内で GetComponent / Find / Camera.main を呼ばない
private Image fill;
private void Awake() => fill = GetComponent<Image>();

// 取得失敗がありうる場面は TryGetComponent
if (TryGetComponent<EnemyBadgeWidget>(out var badge)) badge.Refresh();
```

- `GetComponent` の結果を使うとき、元スキルの `GetComponentInChildren<T>()?.Method()` は **不採用**。`UnityEngine.Object` への `?.` は偽 null を見逃す
- `Camera.main` は内部で検索する。Awake でキャッシュ（`Update` で呼ぶのは 🔴）
- `[SerializeField]` で参照できるものは `GetComponent` しない。Inspector 参照を `GetComponent` に置き換えるのは挙動変更

```csharp
// NG
weapon?.Fire();
// OK
if (weapon != null) weapon.Fire();
```

## 2. ライフサイクル

- `Awake`（自分の初期化）→ `OnEnable` → `Start`（他オブジェクト参照） の順序。**処理を Awake と Start の間で移動しない**（§0-7）
- 空の `Update` / `Start` は削除してよい。中身のあるメッセージの追加・削除・順序変更は禁止
- `OnDestroy` で `Subject.Dispose()`、イベント解除、Tween の Kill（`SetLink` していれば不要）
- イベントの `AddListener` は `Awake`/`Start` で一度だけ。`OnEnable` で足すなら `OnDisable` で外す

## 3. DOTween

```csharp
tween = hpFill.DOFillAmount(hp, 0.25f)
    .SetEase(Ease.OutCubic)
    .SetUpdate(true)          // ポーズ中も動かす場合のみ。元の指定を維持
    .SetLink(gameObject);     // 必須: 破棄で自動 Kill
```

- 新規の Tween は **必ず `.SetLink(gameObject)`**。既存で無いものを足す場合、`SetUpdate(true)` の有無など他の指定は変えない
- 同じ対象の Tween を再生する前に `tween?.Kill()`（`Tween` は純 C# 型なので `?.` 可）
- 演出は `UI/UIFx.cs` の `UIFx.PanelOpen(panelRoot)` / `UIFx.Pop(target)` を再利用し、似た処理を新規に書かない

## 4. 初期非アクティブのポップアップ

```csharp
// NG: Awake 末尾で SetActive(false) すると、初回 Show（SetActive(true) → Awake）が打ち消される
private void Awake()
{
    BuildUi();
    gameObject.SetActive(false);
}
```

非アクティブで始まるオブジェクトの `Awake` は **最初に有効化された時** に走る。そこで `SetActive(false)` を書くと、表示した直後に消える。非表示の初期状態はプレハブ側で持つ。`GamePanelManager` / `PopUpManager` がパネルの表示切り替えを担当している場合は、View 側で勝手に切り替えない。

## 5. Addressables

- ロードは `AddressableLoader.Load/LoadAll` 経由。アドレス・ラベル文字列は変えない（§0-6）
- ロードしたハンドル/アセットは不要になったら解放
- 共有 ScriptableObject はシーン直参照しない。ビルドで複製されるので Addressables から取得（`BattleLifetimeScope` の `SceneData/BattleInputData` が実例）

## 6. UI

- 表示更新は値が変わったときだけ（`text` の毎フレーム代入は避ける）
- `Instantiate` を毎フレーム呼ばない。繰り返し生成するセル（`NewsArticleCellUI`）はプールするか再利用
- `Canvas` の再構築を伴う操作（レイアウトグループ変更）を `Update` で行わない
- 参照未設定でも落ちない（`if (x != null)`）既存の慣習に合わせる

## 7. エディタ専用コード

- `Editor/` フォルダ配下か `#if UNITY_EDITOR`
- ビルドに入る MonoBehaviour から `UnityEditor` 名前空間を参照しない
- `Debug` メニューやデバッグ専用機能は Development Build 限定のことがある（ESC10回リセット等）。勝手に本番へ広げない

## 8. 物理・描画

- `FixedUpdate` で物理、`Update` で入力・UI。移動しない
- `Update` 内での `new` / `string` 補間 / `Find` / LINQ は禁止（[performance-review](performance-review.md)）
- `Time.deltaTime` を使うか `unscaledDeltaTime` を使うかは挙動の核心。リファクタで置き換えない

## 重大度

- 🔴 `UnityEngine.Object` への `?.`、Update 内の GetComponent/Find/Camera.main、初期非アクティブ popup の Awake 末尾 SetActive(false)、Tween の SetLink 漏れ（新規）
- 🟡 TryGetComponent、Awake キャッシュ、UIFx 再利用
- 🟢 Editor 専用コードの整理
