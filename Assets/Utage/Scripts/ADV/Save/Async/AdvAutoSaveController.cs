// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UtageExtensions;

namespace Utage
{
	/// <summary>
	/// 非同期拡張時のオートセーブ制御コンポーネント。AdvSaveManager/AdvSystemSaveDataの
	/// OnApplicationQuit/OnApplicationPauseはここに責務を委譲する（2種類のファイルを1回の
	/// コミットにまとめて処理できるようにするのと、オートセーブ処理自体を拡張しやすくするため）。
	/// トリガーは「一定間隔」「改ページ」「OnApplicationPause」「OnApplicationQuit」の4種で
	/// 個別にオン/オフできる（改ページの方が確実だがスキップ時等の負荷を考慮し既定オフ）。
	/// システムから独立しているので、アレンジした自作コードで制御してもよい
	/// </summary>
	[AddComponentMenu("Utage/ADV/Internal/Async/AdvAutoSaveController")]
	public class AdvAutoSaveController : MonoBehaviour
	{
		AdvEngine Engine => this.GetComponentCacheFindIfMissing(ref engine);
		[SerializeField] AdvEngine engine;

		[Header("Trigger: Interval")]
		[SerializeField] bool enableIntervalTrigger = false;
		[SerializeField] float intervalSeconds = 60f;
		public bool EnableIntervalTrigger { get => enableIntervalTrigger; set => enableIntervalTrigger = value; }
		public float IntervalSeconds { get => intervalSeconds; set => intervalSeconds = value; }

		[Header("Trigger: Page Change")]
		[SerializeField] bool enablePageChangeTrigger = false;
		[SerializeField, Min(1)] int pageChangeInterval = 1;
		public bool EnablePageChangeTrigger { get => enablePageChangeTrigger; set => enablePageChangeTrigger = value; }
		//何ページ進むごとにオートセーブするか
		public int PageChangeInterval { get => pageChangeInterval; set => pageChangeInterval = value; }

		[Header("Trigger: Application Pause / Quit")]
		[SerializeField] bool enableApplicationPauseTrigger = true;
		[SerializeField] bool enableApplicationQuitTrigger = true;
		public bool EnableApplicationPauseTrigger { get => enableApplicationPauseTrigger; set => enableApplicationPauseTrigger = value; }
		public bool EnableApplicationQuitTrigger { get => enableApplicationQuitTrigger; set => enableApplicationQuitTrigger = value; }

		//Updateで経過時間を計測するためのタイマー
		float intervalTimer;

		//改ページトリガーの、直近のオートセーブからの経過ページ数
		int pageChangeCounter;

		protected virtual void OnEnable()
		{
			Engine.OnPageTextChange.AddListener(OnPageTextChange);
			intervalTimer = 0f;
			pageChangeCounter = 0;
		}

		protected virtual void OnDisable()
		{
			Engine.OnPageTextChange.RemoveListener(OnPageTextChange);
		}

		//Updateで一定間隔トリガーを発火する
		protected virtual void Update()
		{
			if (!enableIntervalTrigger) return;
			
			//一定間隔トリガーは「一定時間ごと」に発火する
			intervalTimer += Time.deltaTime;
			if (intervalTimer < intervalSeconds) return;
			intervalTimer = 0f;
			AutoSaveAsync(destroyCancellationToken).FireAndForget();
		}

		//改ページの処理
		void OnPageTextChange(AdvEngine e)
		{
			if(!EnablePageChangeTrigger) return;
			
			//改ページトリガーは「一定ページ数ごと」に発火する
			pageChangeCounter++;
			if (pageChangeCounter < pageChangeInterval) return;
			pageChangeCounter = 0;
			AutoSaveAsync(destroyCancellationToken).FireAndForget();
		}

		//アプリ終了処理（非同期I/Oの完了保証は無いため、不確実なら改ページ等でのトリガーが確実）
		protected virtual void OnApplicationQuit()
		{
			if (!enableApplicationQuitTrigger) return;
			AutoSaveAsync(destroyCancellationToken).FireAndForget();
		}

		//アプリポーズ処理。pauseStatus=true発火の瞬間にフレームループが停止するため、
		//待機してもポーズ前の完了は保証できずFireAndForgetにしている
		//（拡張先の実装次第ではフレームループと無関係に処理が進むこともある）
		protected virtual void OnApplicationPause(bool pauseStatus)
		{
			if (!enableApplicationPauseTrigger || !pauseStatus) return;
			AutoSaveAsync(destroyCancellationToken).FireAndForget();
		}

		/// <summary>
		/// オートセーブ処理
		/// </summary>
		public virtual async Awaitable AutoSaveAsync(CancellationToken cancellationToken)
		{
			var saveExt = Engine.SaveManager as IAdvSaveManagerAsync;
			var sysExt = Engine.SystemSaveData as IAdvSystemSaveDataAsync;
			if (saveExt == null && sysExt == null) return;

			AsyncFileIOQueue queue = saveExt?.AsyncFileIOQueue;
			if (queue == null)
			{
				Debug.LogError($"{nameof(AdvAutoSaveController)} requires {nameof(AsyncFileIOQueue)}", this);
				return;
			}

			//書き込み対象が無ければ空コミットを避ける（Build系は重いので軽いHasXxxTargetで判定）
			bool hasAnyTarget = (saveExt?.HasAutoSaveTarget ?? false) || (sysExt?.HasWriteTarget ?? false);
			if (!hasAnyTarget) return;

			try
			{
				// 2種のオートセーブを1回のコミットにまとめる（別々に呼ぶとコミットが2回に分かれるため）。
				// エントリの生成はdequeue時点（先行処理の完了後）に行い、その時点の最新状態を書き込む
				await queue.WriteFilesAsync(
					() =>
					{
						var files = new List<AsyncFileEntry>(2);
						AsyncFileEntry autoSaveEntry = saveExt?.BuildAutoSaveFileEntry();
						if (autoSaveEntry != null) files.Add(autoSaveEntry);
						AsyncFileEntry systemEntry = sysExt?.BuildWriteFileEntry();
						if (systemEntry != null) files.Add(systemEntry);
						return files;
					},
					AsyncFileIOPriority.AutoSave,
					cancellationToken);
			}
			catch (OperationCanceledException)
			{
				//意図したキャンセルは正常系
			}
			catch (Exception e)
			{
				//例外は握りつぶす（呼び出し元へは伝播させない。失敗1回でゲーム進行を止めたくないため）
				if (this.TryGetComponent(out IAdvSaveExceptionHandler saveExceptionHandler))
				{
					//拡張ハンドラがあれば対応を全て委ねる（デフォルトのログ出力は行わない）
					saveExceptionHandler.OnSaveDataException(AdvSaveOperationType.AutoSave, e);
				}
				else
				{
					//無音にはせず必ずログを残す
					Debug.LogException(e, this);
				}
			}
		}
	}
}
