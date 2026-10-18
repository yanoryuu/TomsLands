# リファクタリング安全ガイド（SKILL.md §0 の詳細）

リファクタリングは **振る舞いを変えない** 変更だけ。ここでは §0 の各項目について「なぜ壊れるか」「変更前に何を確認するか」を示す。コマンドは **Git Bash 形式**、リポジトリルート（`D:\WorkTree\TomsLands\wt-07`）で実行する。`Assets/Scripts` の外（Utage・Plugins・`Packages/`）は触らない。

## 0. 前提: 必ず先に調べること

```bash
# 変更しようとしているシンボルの全参照（コード）
grep -rn "FooBar" Assets --include=*.cs
# シーン/プレハブ/アセットからの文字列参照
grep -rn "FooBar" Assets --include=*.prefab --include=*.unity --include=*.asset
```

クラスの GUID 参照は文字列名では見つからない（次項）。

## 1. シリアライズフィールド（§0-1）

`[SerializeField]`、`public` フィールド、`[Serializable]` クラスのフィールドは **名前と型が YAML に保存される**。リネーム・型変更・削除でシーン/プレハブ/SO の値が消える（エラーも出ない）。

```bash
# フィールド名が YAML に出ているか（フィールド名は : 付きで保存される）
grep -rn "^\s*closeButton:" Assets --include=*.prefab --include=*.unity --include=*.asset
```

- 直したい場合は `[FormerlySerializedAs("旧名")]` を付けるが、**原則やらない**
- `private` で `[SerializeField]` が無いフィールドは保存されないので変更可
- `readonly` や `const` にしてはいけない（シリアライザが値を入れられなくなる）
- 自動プロパティ `public int X { get; set; }` はシリアライズされない。フィールドをプロパティに変えると消える

## 2. MonoBehaviour / ScriptableObject（§0-2）

クラス名とファイル名が一致して **スクリプト GUID（.meta）** が参照を持つ。クラス名変更・ファイル名変更・移動・別ファイルへの分割で参照が切れる（Missing Script）。

```bash
# スクリプトの GUID を調べる
cat Assets/Scripts/Newspaper/NewspaperView.cs.meta | grep guid
# その GUID を使うプレハブ/シーン
grep -rln "guid: <上で得た値>" Assets --include=*.prefab --include=*.unity --include=*.asset
```

- ファイル移動・リネーム・`.meta` の削除をしない
- 1 ファイル内に複数の MonoBehaviour を追加しない

## 3. JSON セーブ対象（§0-3）

`JsonUtility` / Newtonsoft で保存するクラスは、フィールド名がそのまま JSON キー。リネームすると **既存セーブが読めない/既定値で上書きされる**。

```bash
# 保存系の洗い出し
grep -rn "JsonUtility\.\(ToJson\|FromJson\|FromJsonOverwrite\)" Assets/Scripts --include=*.cs
grep -rn "JsonConvert\.\(Serialize\|Deserialize\)" Assets/Scripts --include=*.cs
grep -rn "SaveSlotManager.GetPath" Assets/Scripts --include=*.cs
```

- 対象クラス（例: `NewspaperSaveStore.SaveData`、`ScrapbookModel.Scrap` / `CompanyRecord`）の public フィールドの名前・型・**既定値**を変えない
- `JsonUtility` は private/プロパティ/Dictionary を保存しない。`record`/`init` にしてはいけない
- 新規フィールドの追加は、旧セーブに無いときの既定値が安全であることを確認

## 4. enum（§0-4）

int として保存・シリアライズされる。並べ替え・途中挿入・値の変更は禁止。**追加は末尾のみ**（特に `TomsShopGamePhase`）。

```bash
grep -rn "enum TomsShopGamePhase" Assets/Scripts --include=*.cs
# enum が YAML 上で数値として保存されているか（シーン/プレハブ内の整数値）
grep -rn "phase:" Assets --include=*.prefab --include=*.unity --include=*.asset | head
```

- `(int)` キャスト、`Enum.Parse`、`ToString()` で文字列保存している箇所がないか grep（`ToString()` 保存ならメンバー名の変更もセーブ互換を壊す）

## 5. UnityEvent から呼ばれるメソッド（§0-5）

`Button.onClick` などのインスペクタ登録は、`m_MethodName: メソッド名` で YAML に保存される。メソッドのリネーム・`public` を外す・引数の変更で **黙って呼ばれなくなる**。未使用に見える `public` メソッドは特に危険。

```bash
# 変更前に必ず確認
grep -rn "m_MethodName: OnClickStart" Assets --include=*.prefab --include=*.unity
# 呼ばれ先のコンポーネントを特定（m_TargetAssemblyTypeName にクラス名が出る）
grep -rn -B3 "m_MethodName: OnClickStart" Assets --include=*.prefab --include=*.unity
# Animation Event（関数名が functionName に入る）
grep -rn "functionName: OnHit" Assets --include=*.anim --include=*.controller --include=*.prefab
```

- `SendMessage` / `Invoke` / `InvokeRepeating` / `StartCoroutine("名前")` は文字列でメソッドを呼ぶ:

```bash
grep -rn "SendMessage\|BroadcastMessage\|Invoke(\"\|InvokeRepeating(\"\|StartCoroutine(\"" Assets/Scripts --include=*.cs
```

## 6. 文字列キー（§0-6）

| 種別 | 確認 |
|---|---|
| Addressables アドレス/ラベル | `grep -rn "AddressableLoader\.\(Load\|LoadAll\)" Assets/Scripts --include=*.cs`、`Assets/AddressableAssetsData` の設定 |
| PlayerPrefs | `grep -rn "PlayerPrefs\." Assets/Scripts --include=*.cs` |
| RemoteConfig | `grep -rn "RemoteConfig" Assets/Scripts --include=*.cs` |
| TSV 列名・マスタ | `grep -rn "\.tsv\|TSV" Assets/Scripts --include=*.cs`、マスタ側ヘッダー |
| Utage ラベル/コマンド | `grep -rn "Utage\|Label" Assets/Scripts --include=*.cs` とシナリオ（.xls/.tsv）側 |
| Animator パラメータ | `grep -rn "SetTrigger\|SetBool\|SetFloat\|SetInteger" Assets/Scripts --include=*.cs`、`.controller` 内の名前 |

これらの文字列を `nameof` や定数に置き換えるときは、**値が一文字も変わらない**ことを確認する（クラス名リネームで `nameof` の値が変わる事故に注意）。

## 7. Unity メッセージ（§0-7）

`Awake/Start/OnEnable/OnDisable/Update/OnDestroy` の追加・削除・移動・順序変更は実行順を変える。

- `Awake` と `Start` の間で処理を移さない（他オブジェクトの初期化タイミングに依存）
- 空のメッセージは消してよいが、**中身があるものを消す/統合する場合は呼び出し順を確認**
- 基底クラスの `virtual` メソッドを `override` に変えるときは `base.` 呼び出しを残す

```bash
grep -rn "void \(Awake\|Start\|OnEnable\|OnDisable\|OnDestroy\)" Assets/Scripts/<対象ディレクトリ> --include=*.cs
```

## 8. DI 登録順・購読順・初期化順（§0-8）

```bash
grep -n "Register" Assets/Scripts/GameLifetimeScope.cs
grep -rn "RegisterOnEnter" Assets/Scripts --include=*.cs
```

- `GameLifetimeScope` の登録行の並べ替え禁止
- `StateManager.RegisterOnEnter(...)` はコンストラクタ内の呼び出し順がフェーズ入口の実行順になる（`StateManager` / `GameLifecycleHandler` / `TomsShopPresenter.Entry` は順序依存）
- コンストラクタの購読を `Start` に移すと順序が変わる
- `Subscribe` の追加・削除・並べ替えは通知順を変える

## 9. セーブ呼び出し（§0-9）

「状態変更 → Save」の対を壊さない。メソッドを分割/統合するときは Save の位置を維持する。

```bash
grep -rn "Save()\|\.Save(" Assets/Scripts --include=*.cs
```

## 10. 公開 API（§0-10）

```bash
# メソッド/プロパティ名の全呼び出し元
grep -rn "\.MethodName(" Assets --include=*.cs
grep -rn "MethodName" Assets --include=*.cs --include=*.prefab --include=*.unity
# 型の使用
grep -rn "\bNewspaperView\b" Assets --include=*.cs
```

- 呼び出し元が担当外ファイルに及ぶなら **変えずに報告**
- Editor/Debug コード（`Debug/`, `Editor/`）・リフレクションからの参照も確認
- 追加の参照: `Assets/Scripts` 外の asmdef が無いので、名前空間なしのクラスは `Assets/` 全体が対象

## 11. 外部コード（§0-11）

`Assets/Scripts` 外（Utage、Plugins、サードパーティ、`Packages/`）は変更しない。

## 変更前の最終確認リスト

```bash
# 1. 変更ファイル一覧が担当範囲内か
git diff --stat
# 2. シリアライズ属性の行が差分に出ていないか
git diff | grep -n "^[-+].*\(SerializeField\|Serializable\|FormerlySerializedAs\)"
# 3. クラス宣言行（名前変更・基底クラス変更）が差分に出ていないか
git diff | grep -n "^[-+].*\(class \|enum \|struct \)"
# 4. 文字列リテラルの変更がないか
git diff | grep -n '^[-+].*"[^"]*"'
```

差分に上記が現れたら、意図したものか一件ずつ確認する。

## 迷ったときの方針

- 変えない。気づいた不具合・仕様変更の余地は **報告** する（リファクタに混ぜない）
- 「たぶん未使用」は grep で証明できるまで消さない
- 変更後は Unity のコンパイルエラー/警告ゼロと、`Player.log` / コンソールに新しい警告が出ていないことを確認する
