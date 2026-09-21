// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Utage
{
	/// <summary>
	/// AdvSystemSaveDataの非同期拡張（継承版）。既存コンポーネントの代わりにアタッチする
	/// （Inspector値の移行は`GameObject/Utage/Convert To Async Save`メニューを使うこと）。
	/// バイナリ生成は継承元のロジックをそのまま使い、実ファイルI/OだけをIAsyncFileIO実装に委譲する。
	/// </summary>
	[AddComponentMenu("Utage/ADV/Internal/Async/AdvSystemSaveDataAsync")]
	public class AdvSystemSaveDataAsync : AdvSystemSaveData, IAdvSystemSaveDataAsync
	{
		//実ファイルI/O拡張。FileIOManagerと同じGameObjectにアタッチされたAsyncFileIOQueueを探す
		public AsyncFileIOQueue AsyncFileIOQueue
		{
			get
			{
				if (asyncFileIOQueue == null)
				{
					asyncFileIOQueue = FileIOManager.GetComponent<AsyncFileIOQueue>();
				}
				return asyncFileIOQueue;
			}
		}
		AsyncFileIOQueue asyncFileIOQueue;

		void Awake()
		{
			if (AsyncFileIOQueue == null)
			{
				//FileIOManagerと同じGameObjectに、AsyncFileIOQueueが必要
				Debug.LogError($"{nameof(AdvSystemSaveDataAsync)} requires {nameof(AsyncFileIOQueue)}", FileIOManager);
			}
		}

		//セーブデータのディレクトリ作成とPathの確定（読み込みの前処理）の非同期版
		protected override void EnsureSaveDirAndPath()
		{
			//非同期版はSdkPersistentDataPathを使わずディレクトリ名だけ設定する
			//（ルート作成等はIAsyncFileIO実装側に委譲）
			Path = FilePathUtil.Combine(DirectoryName, FileName);
		}

		//終了時・ポーズ時のオートセーブは、AdvAutoSaveController側の責務とし、ここでは何もしない
		protected override void OnApplicationQuit()
		{
		}
		protected override void OnApplicationPause(bool pauseStatus)
		{
		}

		//同期Write()は非同期拡張時には呼ばれるべきではない
		public override void Write()
		{
			Debug.LogError($"{nameof(AdvSystemSaveDataAsync)}使用時に同期の{nameof(Write)}()が呼ばれました。"
				+ $"呼び出し元を非同期経路（{nameof(WriteAsync)}）に対応させてください。", this);
		}

		//同期Init()も同様（想定外経路からの呼び出しをエラーで検知する）
		public override void Init(AdvEngine engine)
		{
			Debug.LogError($"{nameof(AdvSystemSaveDataAsync)}使用時に同期の{nameof(Init)}()が呼ばれました。"
				+ $"呼び出し元を非同期経路（{nameof(InitAsync)}）に対応させてください。", this);
		}

		//同期Delete()も同様（IAdvSaveDelete.OnDeleteAllSaveDataAndQuit経由で呼ばれうる）
		public override void Delete()
		{
			Debug.LogError($"{nameof(AdvSystemSaveDataAsync)}使用時に同期の{nameof(Delete)}()が呼ばれました。"
				+ $"呼び出し元を非同期経路（{nameof(AdvSaveManagerAsync)}.{nameof(AdvSaveManagerAsync.DeleteAllSaveDataAndSystemDataAsync)}）に対応させてください。", this);
		}

		//初期化。ファイル読み込みだけを非同期で行う
		public async Awaitable InitAsync(AdvEngine engine, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			this.engine = engine;
			bool loaded = false;
			if (!DontUseSystemSaveData)
			{
				EnsureSaveDirAndPath();
				if (AsyncFileIOQueue == null)
				{
					throw new InvalidOperationException($"{nameof(AdvSystemSaveDataAsync)} requires {nameof(AsyncFileIOQueue)}");
				}
				loaded = await ReadCoreAsync(cancellationToken);
			}
			if (!loaded) InitDefault();
			isInit = true;
		}

		//システムセーブデータを非同期で書き込む
		public async Awaitable WriteAsync(AsyncFileIOPriority priority, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!HasWriteTarget) return;
			await WriteCoreAsync(priority, cancellationToken);
		}

		//InitAsyncの内部実装（実ファイルの読み込み処理）
		async Awaitable<bool> ReadCoreAsync(CancellationToken cancellationToken)
		{
			if (AsyncFileIOQueue == null)
			{
				throw new InvalidOperationException($"{nameof(AdvSystemSaveDataAsync)} requires {nameof(AsyncFileIOQueue)}");
			}
			//ファイル不存在（正常系、InitDefaultでよい）と読み込み・デコード失敗（一時的I/Oエラーの
			//可能性があるデータ破損）は区別し、後者は握りつぶさず呼び出し元まで伝播させる
			IReadOnlyList<AsyncFileEntry> files = await AsyncFileIOQueue.ReadFilesAsync(new[] { Path }, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			if (!files[0].Exists) return false;
			byte[] decoded = FileIOManager.Decode(files[0].Bytes);
			using (var stream = new MemoryStream(decoded))
			using (var reader = new BinaryReader(stream))
			{
				ReadBinary(reader);
			}
			return true;
		}

		//書き込み対象があるか
		public bool HasWriteTarget => !DontUseSystemSaveData && isInit;

		//実ファイルI/Oを行わず、書き込み用のバイナリだけを生成する
		public AsyncFileEntry BuildWriteFileEntry()
		{
			if (!HasWriteTarget) return null;
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream))
			{
				WriteBinary(writer);
				byte[] encoded = FileIOManager.Encode(stream.ToArray());
				return new AsyncFileEntry(Path, encoded);
			}
		}

		//WriteAsyncの内部実装（実ファイルの書き込み処理）
		async Awaitable WriteCoreAsync(AsyncFileIOPriority priority, CancellationToken cancellationToken)
		{
			if (AsyncFileIOQueue == null)
			{
				throw new InvalidOperationException($"{nameof(AdvSystemSaveDataAsync)} requires {nameof(AsyncFileIOQueue)}");
			}
			await AsyncFileIOQueue.WriteFilesAsync(
				() => new[] { BuildWriteFileEntry() },
				priority,
				cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
		}
	}
}
