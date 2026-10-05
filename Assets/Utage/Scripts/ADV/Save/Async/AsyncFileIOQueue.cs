// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Utage
{
	/// <summary>
	/// IAsyncFileIO実装の手前で読み書き全体を単一の優先度付きキューで排他する中継クラス
	/// （ファイル単位ではなくセッション単位。Platform Toolkitの`ISaveReadable`/`ISaveWritable`は
	/// 1つでも開いていると他の操作もできなくなる制約を踏まえた保守的な設計）。
	/// 自身はI/Oを持たず、実行時に同じGameObjectのIAsyncFileIO実装を使用する
	/// </summary>
	[AddComponentMenu("Utage/ADV/Internal/Async/AsyncFileIOQueue")]
	public class AsyncFileIOQueue : MonoBehaviour
	{
		public IAsyncFileIO AsyncFileIO
		{
			get
			{
				if (asyncFileIO == null)
				{
					asyncFileIO = GetComponent<IAsyncFileIO>();
				}
				return asyncFileIO;
			}
		}
		IAsyncFileIO asyncFileIO;
		//現在キューが要求を実行中かどうか
		public bool IsRunning { get; private set; }
		
		//優先度2段階（Explicit＞AutoSave、同一優先度内はFIFO（先入れ先出し））。
		//実行中の要求は遅らせずにそのまま実行される。
		readonly Queue<AwaitableCompletionSource> explicitWaiters = new Queue<AwaitableCompletionSource>();
		readonly Queue<AwaitableCompletionSource> autoSaveWaiters = new Queue<AwaitableCompletionSource>();

		void Awake()
		{
			if (AsyncFileIO == null)
			{
				//同じGameObjectに、IAsyncFileIOを実装した非同期IO用のコンポーネントが必要
				Debug.LogError($"{nameof(AsyncFileIOQueue)} requires {nameof(IAsyncFileIO)}", this);
			}
		}

		/// <summary>
		/// 指定のファイルをまとめて読み込む。
		/// 読み込みは常に明示的操作として扱うため優先度パラメータ不要
		/// </summary>
		public async Awaitable<IReadOnlyList<AsyncFileEntry>> ReadFilesAsync(
			IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
		{
			await EnterAsync(AsyncFileIOPriority.Explicit, cancellationToken);
			try
			{
				if (AsyncFileIO == null)
				{
					throw new InvalidOperationException($"{nameof(AsyncFileIOQueue)} requires {nameof(IAsyncFileIO)}");
				}
				return await AsyncFileIO.ReadFilesAsync(paths, cancellationToken);
			}
			finally
			{
				ExitAndRunNext();
			}
		}

		/// <summary>
		/// 複数ファイルをまとめて書く。
		/// buildFilesはdequeue時点（先行する処理の完了後）に呼ばれる点に注意。
		/// </summary>
		public async Awaitable WriteFilesAsync(
			Func<IReadOnlyList<AsyncFileEntry>> buildFiles, AsyncFileIOPriority priority,
			CancellationToken cancellationToken)
		{
			await EnterAsync(priority, cancellationToken);
			try
			{
				if (AsyncFileIO == null)
				{
					throw new InvalidOperationException($"{nameof(AsyncFileIOQueue)} requires {nameof(IAsyncFileIO)}");
				}
				await AsyncFileIO.WriteFilesAsync(buildFiles(), cancellationToken);
			}
			finally
			{
				ExitAndRunNext();
			}
		}

		/// <summary>
		/// 指定のファイルをまとめて削除
		/// 読み書きと同じキューで排他する。常に明示的操作として扱う
		/// </summary>
		public async Awaitable DeleteFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
		{
			await EnterAsync(AsyncFileIOPriority.Explicit, cancellationToken);
			try
			{
				if (AsyncFileIO == null)
				{
					throw new InvalidOperationException($"{nameof(AsyncFileIOQueue)} requires {nameof(IAsyncFileIO)}");
				}
				await AsyncFileIO.DeleteFilesAsync(paths, cancellationToken);
			}
			finally
			{
				ExitAndRunNext();
			}
		}


		async Awaitable EnterAsync(AsyncFileIOPriority priority, CancellationToken cancellationToken)
		{
			if (IsRunning)
			{
				var cs = new AwaitableCompletionSource();
				(priority == AsyncFileIOPriority.Explicit ? explicitWaiters : autoSaveWaiters).Enqueue(cs);
				//完了した時点で実行権（IsRunning）は既に自分に譲渡されている（ExitAndRunNext参照）
				await cs.Awaitable;
			}
			else
			{
				IsRunning = true;
			}

			if (cancellationToken.IsCancellationRequested)
			{
				//キャンセルされても実行権を握ったまま抜けるとキューが詰まるため、必ず次に譲ってから例外を投げる
				ExitAndRunNext();
				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		void ExitAndRunNext()
		{
			if (explicitWaiters.Count > 0)
			{
				explicitWaiters.Dequeue().SetResult();
			}
			else if (autoSaveWaiters.Count > 0)
			{
				autoSaveWaiters.Dequeue().SetResult();
			}
			else
			{
				IsRunning = false;
			}
		}
	}
}
