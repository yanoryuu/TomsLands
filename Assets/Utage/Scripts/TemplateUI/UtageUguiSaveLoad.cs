// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Profiling;
using Utage;
using UtageExtensions;

namespace Utage
{

	/// <summary>
	/// セーブロード画面のサンプル
	/// </summary>
	[AddComponentMenu("Utage/TemplateUI/UtageUguiSaveLoad")]
	public class UtageUguiSaveLoad : UguiView
	{
		[SerializeField] protected UguiGridPage gridPage;

		/// <summary>
		/// リストビューアイテムのリスト
		/// </summary>
		protected List<AdvSaveData> itemDataList;

		/// <summary>ADVエンジン</summary>
		public virtual AdvEngine Engine => this.GetAdvEngineCacheFindIfMissing(ref engine);
		[SerializeField] protected AdvEngine engine;

		/// <summary>メイン画面</summary>
		public UtageUguiMainGame mainGame;

		/// <summary>タイトル表記（セーブ画面かロード画面か）</summary>
		public GameObject saveRoot;

		/// <summary>タイトル表記（セーブ画面かロード画面か）</summary>
		public GameObject loadRoot;

		//ガイドメッセージの表示。設定してないときは表示しない
		public SystemUiGuideMessage guideMessage;
		//上書きセーブの確認ダイアログの表示。設定してないときは表示しない
		public SystemUiDialog2Button dialog;

		//ロード後、画面を閉じるまでの待機時間
		public float waitTimeOnLoad = 0;

		//セーブ画面か、ロード画面かの区別
		public bool IsSave => isSave;
		protected bool isSave;

		protected bool isInit = false;
		protected int lastPage;

		CancellationTokenSource waitOpenCts;

		/// <summary>
		/// セーブ画面を開く
		/// </summary>
		/// <param name="prev">前の画面</param>
		public virtual void OpenSave(UguiView prev)
		{
			isSave = true;
			saveRoot.SetActive(true);
			loadRoot.SetActive(false);
			Open(prev);
		}

		/// <summary>
		/// ロード画面を開く
		/// </summary>
		/// <param name="prev">前の画面</param>
		public virtual void OpenLoad(UguiView prev)
		{
			isSave = false;
			saveRoot.SetActive(false);
			loadRoot.SetActive(true);
			Open(prev);
		}

		/// <summary>
		/// オープンしたときに呼ばれる
		/// </summary>
		protected virtual void OnOpen()
		{
			isInit = false;
			this.gridPage.ClearItems();
			if (Engine.SaveManager is IAdvSaveManagerAsync)
			{
				//画面を閉じたら（OnCloseで）続きは不要になる操作なので、Destroy時だけでなく
				//Close時にもキャンセルされるトークンを使う（保存処理と違い、待たれてもいないので
				//生き残らせる意味が無い）
				waitOpenCts?.Cancel();
				waitOpenCts?.Dispose();
				waitOpenCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
				WaitOpenAsync(waitOpenCts.Token).FireAndForget();
			}
			else
			{
				StartCoroutine(CoWaitOpen());
			}
		}

		/// <summary>
		/// クローズしたときに呼ばれる
		/// </summary>
		protected virtual void OnClose()
		{
			lastPage = gridPage.CurrentPage;
			this.gridPage.ClearItems();
			waitOpenCts?.Cancel();
			waitOpenCts?.Dispose();
			waitOpenCts = null;
		}

		//起動待ちしてから開く（セーブファイルの読み込み同期処理）
		protected virtual IEnumerator CoWaitOpen()
		{
			while (Engine.IsWaitBootLoading)
			{
				yield return null;
			}

			Engine.SaveManager.ReadAllSaveData();
			SetupItemsAfterRead();
		}

		//起動待ちしてから開く（セーブファイルの読み込みは非同期処理）
		protected virtual async Awaitable WaitOpenAsync(CancellationToken cancellationToken)
		{
			await UtageExtensionMethodsAwaitable.UntilAsync(() => !Engine.IsWaitBootLoading, cancellationToken);

			var asyncExtension = (IAdvSaveManagerAsync)Engine.SaveManager;
			try
			{
				await asyncExtension.ReadAllSaveDataAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception e)
			{
				if (this.TryGetComponent(out IAdvSaveExceptionHandler saveExceptionHandler))
				{
					//拡張ハンドラがあれば例外への対応を全て委ねる（デフォルトのガイドメッセージは出さない）
					saveExceptionHandler.OnSaveDataException(AdvSaveOperationType.OpenSaveLoadList, e);
				}
				else
				{
					//あえて例外を投げ直さず握りつぶす。
					//投げ直すと後続の一覧構築（gridPage.CreateItems等）が丸ごと止まり画面自体が開けなくなるため
					//ここが例外の終端になる（FireAndForgetまで伝播させないので、ログもここで責任を持って出す）
					//例外処理をしたい場合は上記のIAdvSaveExceptionHandlerを実装
					Debug.LogException(e, this);
					
					//ロード失敗としてガイドメッセージを表示
					if (guideMessage != null)
					{
						guideMessage.Open(LanguageSystemText.LocalizeText(SystemText.UtageGuideMessageSaveDataLoadFailed));
					}
				}
			}

			SetupItemsAfterRead();
		}

		//セーブデータ読み込み後、一覧の表示アイテムを構築する（同期・非同期共通の後処理）
		protected virtual void SetupItemsAfterRead()
		{
			AdvSaveManager saveManager = Engine.SaveManager;
			List<AdvSaveData> list = new List<AdvSaveData>();
			if (saveManager.IsAutoSave) list.Add(saveManager.AutoSaveData);
			list.AddRange(saveManager.SaveDataList);
			this.itemDataList = list;
			gridPage.Init(itemDataList.Count, CallBackCreateItem);
			gridPage.CreateItems(lastPage);
			isInit = true;
		}


		/// <summary>
		/// リストビューのアイテムが作成されるときに呼ばれるコールバック
		/// </summary>
		/// <param name="go">作成されたアイテムのGameObject</param>
		/// <param name="index">作成されたアイテムのインデックス</param>
		protected virtual void CallBackCreateItem(GameObject go, int index)
		{
			UtageUguiSaveLoadItem item = go.GetComponent<UtageUguiSaveLoadItem>();
			AdvSaveData data = itemDataList[index];
			if (guideMessage != null)
			{
				//確認ダイアログやガイドメッセージを表示する場合はこっち
				//ボタンクリックのコールバックを設定しないので、ボタンプレハブのインスペクター上で設定しておくこと
				item.Init(data, index, isSave);
			}
			else
			{
				//確認ダイアログやガイドメッセージを表示しない場合はこっち
				item.Init(data, OnTap, index, isSave);
			}
		}

		protected virtual void Update()
		{
			//右クリックで戻る
			if (isInit && InputUtil.IsInputGuiClose())
			{
				Back();
			}
		}


		/// <summary>
		/// 各アイテムが押された
		/// </summary>
		/// <param name="item">押されたアイテム</param>
		public virtual void OnTap(UtageUguiSaveLoadItem item)
		{
			if (isSave)
			{
				//セーブ画面なら、セーブ処理
				WriteSaveDataAndRefresh(item);
			}
			else
			{
				//ロード画面
				if (item.Data.IsSaved)
				{
					//セーブ済みのデータならこの画面は閉じてロードをする
					if (waitTimeOnLoad <= 0)
					{
						Close();
						mainGame.OpenLoadGame(item.Data);
					}
					else
					{
						mainGame.OpenLoadGame(item.Data);
						StartCoroutine(CoWaitOnLoad(item));
					}
				}
			}
		}


		protected virtual IEnumerator CoWaitOnLoad(UtageUguiSaveLoadItem item)
		{
			this.StoreAndChangeCanvasGroupInput(false);
			yield return new WaitForSeconds(waitTimeOnLoad);
			this.RestoreCanvasGroupInput();
			Close();
		}

		/// <summary>
		/// セーブ処理の共通処理（非同期拡張がある場合は非同期で書き込み、無い場合は従来通り同期で書き込む）
		/// </summary>
		protected virtual void WriteSaveDataAndRefresh(UtageUguiSaveLoadItem item)
		{
			if (Engine.SaveManager is IAdvSaveManagerAsync)
			{
				//SaveManagerに非同期拡張がある場合は、非同期で書き込みを行う（画面を閉じても書き込みは完了させるため）
				WriteSaveDataAndRefreshAsync(item).FireAndForget();
			}
			else
			{
				//無ければ従来通り同期で行う。
				Engine.WriteSaveData(item.Data);
				item.Refresh(true);
			}
		}

		//セーブ処理（非同期版）
		protected virtual async Awaitable WriteSaveDataAndRefreshAsync(UtageUguiSaveLoadItem item)
		{
			try
			{
				await Engine.WriteSaveDataAsync(item.Data, destroyCancellationToken);
				if (item == null) return;
				item.Refresh(true);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				if (this.TryGetComponent(out IAdvSaveExceptionHandler saveExceptionHandler))
				{
					//拡張ハンドラがあれば例外への対応を全て委ねる（デフォルトのガイドメッセージは出さない）
					saveExceptionHandler.OnSaveDataException(AdvSaveOperationType.Save, e);
				}
				else
				{
					//セーブ失敗としてガイドメッセージを表示
					if (guideMessage != null)
					{
						guideMessage.Open(LanguageSystemText.LocalizeText(SystemText.UtageGuideMessageSaveFailed));
					}
					throw;
				}
			}
		}

		// 各アイテムが押された
		// 宴4以降 
		public virtual void OnClicked(UtageUguiSaveLoadItem item)
		{
			if (isSave)
			{
				//セーブ画面の処理

				if (item.Data.Type == AdvSaveData.SaveDataType.Auto)
				{
					//オートセーブならセーブでできない
					if (guideMessage != null)
					{
						guideMessage.Open(LanguageSystemText.LocalizeText(SystemText.UtageGuideMessageSaveFailedAutoSave));
					}
				}
				else
				{
					void WriteSaveData()
					{
						//セーブ画面なら、セーブ処理
						WriteSaveDataAndRefresh(item);
					}

					if (item.Data.IsSaved)
					{
						//既にセーブされている
						if (dialog != null)
						{
							//上書き確認ダイアログを表示
							dialog.OpenYesNo(
								LanguageSystemText.LocalizeText(SystemText.UtageDialogMessageSaveConfirm), WriteSaveData,
								() => { });
						}
						else
						{
							WriteSaveData();
						}
					}
					else
					{
						WriteSaveData();
					}

				}
			}
			else
			{
				//ロード画面の処理
				if (item.Data.IsSaved)
				{
					//セーブ済みのデータならこの画面は閉じてロードをする
					if (waitTimeOnLoad <= 0)
					{
						Close();
						mainGame.OpenLoadGame(item.Data);
					}
					else
					{
						mainGame.OpenLoadGame(item.Data);
						StartCoroutine(CoWaitOnLoad(item));
					}
				}
				else
				{
					//セーブされていないデータなら、エラーメッセージを表示する
					if (guideMessage!=null)
					{
						guideMessage.Open(LanguageSystemText.LocalizeText(SystemText.UtageGuideMessageLoadFailedNotSaved));
					}
				}
			}
		}
	}
}
