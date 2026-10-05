# TomsLands ゲームマップ（委譲時の参照用）

## 目次
1. 環境
2. システム別ディレクトリ
3. 高リスク領域
4. アーキテクチャ規約・新規画面の手順
5. 衝突しやすいファイル
6. 検証手段
7. Docs

## 1. 環境
Unity 6000.0.32f1 / URP / uGUI+TMP / DOTween / VContainer 1.17 / R3 / UniTask（UniRxは不使用）/ Addressables / Utage / Firebase Remote Config。asmdefなし（全てAssembly-CSharp）。解像度1920x1080。

## 2. システム別ディレクトリ（Assets/Scripts/）
| 領域 | ディレクトリ | 内容 |
|---|---|---|
| 中核 | 直下 | GameLifetimeScope（DIルート）, StateManager, GamePanelManager, GameLifecycleHandler, AddressableLoader, GameConst |
| フロー | GameFlow/ | ラン自動生成（シード再現）, TurnPhaseManager（イベント→仕入れ→陳列→営業） |
| 店 | TomsShop/ | ItemModel（価格・需要経済）, 陳列, 営業, バズ, Market/（ABM価格エンジン）, 店レベル, マシン, 朝レポート |
| 仕入れ | BlackSmith/ | トレード端末風UI, PriceChartView, お任せ仕入れ |
| 情報 | Information/, Prophet/, Newspaper/, Map/ | 情報屋・占い師・新聞（5社）・ダンジョン情報 |
| 金融 | Finance/ | ファンド・債券・配当武器 |
| 勇者/配信 | Hero/, Battle/, Streaming/, StreamingSetting/, SceneData/ | クリア確率, 配信戦闘, 販売ループ, 精算, シーン間受け渡し |
| マーケ | Marketing/ | 広告・バズ・フォロワー |
| メタ | Relic/, Preparation/, Village/, Save/ | レリック, 準備, 村, セーブスロット |
| 他 | Scenario/(Utage), Debug/(F12), Editor/, Title/, Result/, GameOver/, Event/, Popup/, Boot/, RemoteConfig/, Config/ | |

## 3. 高リスク領域（opus必須・変更時は理由を報告させる）
- **StateManager / GameLifecycleHandler**: 初期化順依存。帰還時処理は GameLifecycleHandler → BattleResultHandler の登録順に依存。VContainerでは GameFlowManager.Start が先に走る
- **TomsShopPresenter.Entry**: 新聞→保留イベント→借金→朝レポートの順序に意味あり。朝レポートは表示で消費
- **セーブ**: 状態変更後の保存漏れバグが頻発。各Modelはコンストラクタでロード → CurrentSlotはタイトルで確定済みが前提。metaData.jsonはラン削除対象外
- **配信精算**: StopSales 前にスナップショットを取ると売上が消える
- **TomsShopGamePhase enum**: 必ず末尾に追加（intシリアライズがずれる）
- **シーン/プレハブYAML**: TomsShop.unity は直接いじらずプレハブ側で編集
- 初期非アクティブなポップアップで Awake 末尾に SetActive(false) を書くと初回Showが打ち消される
- ファイルの空スタブ上書き事故の前例あり（VillageLifetimeScope.cs）→ 差分確認必須

## 4. アーキテクチャ規約・新規画面の手順（お手本: Newspaper/）
MVP: Model=Singleton / View=MonoBehaviour（R3 Subject公開）/ Presenter=IStartable+IDisposable のEntryPoint。新規UIは未配線でも起動できるnull-safeが慣習。

1. TomsShopGamePhase の**末尾**に値を追加
2. XxxView.cs（SerializeField + Subject）
3. XxxPresenter.cs（ctorで `stateManager.RegisterOnEnter(phase, Entry)`、閉じる時 `ChangeTomsShopPhase(returnPhase)`）
4. GamePanelManager に xxxPanel を追加（HideAll と switch、CommonView表示可否）
5. GameLifetimeScope に SerializeField + `RegisterComponentSafe`、Presenterは View が null なら登録しない
6. Assets/Prefabs/Screens/Xxx.prefab を作りシーンに配置・配線
7. 遷移前に `StateManager.HasHandler()` で確認

その他:
- Addressables: `AddressableLoader.Load/LoadAll`。アドレス=旧Resources相対パス、ラベル=型名、置き場 Assets/Resources_moved/、登録はメニュー「Tools/TomsLands/Addressables一括登録（Resources_moved）」
- 調整値: GameConst → Config/GameConstSettings(SO)。SOロード直後に `RemoteBalance.ApplyOverwrite/ApplyList`。マスターTSVはタブ区切りで拡張子.csv
- セーブパス: `SaveSlotManager.GetPath()` 経由（persistentDataPath/slot_N/）
- Utage: `ScenarioPlayer.PlayAsync(label)` のみ。Canvas Overlay/sortingOrder 2000、Layerシート型はDummy
- 演出: UI/UIFx.cs の PanelOpen/Pop。Tweenには必ず `SetLink(gameObject)`

## 5. 衝突しやすいファイル（同時に1体のみ）
- シーン: Assets/Scene/TomsShop.unity, VillageScene.unity, TitleScene.unity
- 共有プレハブ: Prefabs/Screens/BlackSmith.prefab, CommonCanvas.prefab, TomsShop.prefab
- コード: GameLifetimeScope.cs, GamePanelManager.cs, TomsShopGamePhase.cs, StateManager.cs, TomsShopPresenter.cs, ItemModel.cs, GameConstData.cs, RemoteBalance.cs
- Addressables: AddressableAssetSettings.asset, AssetGroups/Default Local Group.asset
- 設定: ProjectSettings/EditorBuildSettings.asset, GameConstSettings.asset

## 6. 検証手段
- unity-editor-mcp: `recompile`/`recompile_status`, `console`/`clear_console`, `editor_play`/`editor_stop`, `capture_game_view`/`screenshot`, `get_scene_hierarchy`/`find_gameobjects`, `get/set_serialized_field`, `save_prefab_contents`, `eval`/`eval_file`, `run_tests`, `build`
- 注意: エディタ非フォーカスだとPlay/コンパイルが進まない（`editor_focus`）。ScreenSpaceOverlay Canvasはキャプチャに写らない。evalではDIインスタンスを取れない
- テストコードは無し。F12 DebugMenu で所持金・NextTurn・バズを操作可能
- 旧UnityMCP（CoplayDev）は不安定。unity-editor-mcp を優先

## 7. Docs
- 配線手順: Docs/*_UnityWiring.md（新機能はこの形式で残す）
- 仕様: News_Spec, News_UIParts（パレット）, Village_Meta_Design, Market_Price_v2_Design, ShopEconomy, GameConst_RemoteConfig_Spec, GAS_*_Spec, DebugMenu_Spec
- 制作: ScreenRedesign_BuildPrompts, 素材発注リスト_2026-09, mockups/
- データ: balance_tsv/, items_sheet.tsv
