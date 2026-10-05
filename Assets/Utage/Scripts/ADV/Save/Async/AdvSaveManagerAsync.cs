// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Utage
{
	/// <summary>
	/// AdvSaveManagerの非同期拡張（継承版）。既存コンポーネントの代わりにアタッチする
	/// （Inspector値の移行は`GameObject/Utage/Convert To Async Save`メニューを使うこと）。
	/// バイナリ生成は継承元のロジックをそのまま使い、実ファイルI/OだけをIAsyncFileIO実装に委譲する。
	/// </summary>
	[AddComponentMenu("Utage/ADV/Internal/Async/AdvSaveManagerAsync")]
	public class AdvSaveManagerAsync : AdvSaveManager, IAdvSaveManagerAsync
	{
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
			//AdvSaveManagerAsyncとAdvSystemSaveDataAsyncは常にセットで使う前提。組み合わせ忘れは
			//構成ミスとして早期にエラーで知らせる
			if (!this.TryGetComponent<IAdvSystemSaveDataAsync>(out _))
			{
				Debug.LogError(
					$"{nameof(AdvSaveManagerAsync)}を使う場合は"
					+ $"AdvSystemSaveDataも{nameof(AdvSystemSaveDataAsync)}に差し替えてください。"
					+ "このままでは非同期セーブ時にシステムセーブデータが書き込まれません。", this);
			}

			if (AsyncFileIOQueue == null)
			{
				//FileIOManagerと同じGameObjectに、AsyncFileIOQueueが必要
				Debug.LogError($"{nameof(AdvSaveManagerAsync)} requires {nameof(AsyncFileIOQueue)}", FileIOManager);
			}
		}

		//セーブデータのディレクトリ作成とPathの確定（読み込みの前処理）の非同期版
		protected override void EnsureSaveDir()
		{
			//既定のFileIOManager.CreateDirectoryをそのまま呼ぶと、
			//ディレクトリ概念を持たないバックエンド（Platform Toolkit等）で不要なローカルディレクトリが作られてしまうため、ここでは呼ばない
			//実ディレクトリの作成が必要であれば、IAsyncFileIO実装側の責務とする。
		}
		
		//ディレクトリパスの取得
		protected override string ToDirPath()
		{
			//非同期版はSdkPersistentDataPathを使わずディレクトリ名だけ設定する
			//（ルート作成等はIAsyncFileIO実装側に委譲）
			return DirectoryName + "/";
		}

		//同期の読み書きメソッドは非同期拡張時には呼ばれるべきではない
		public override void WriteSaveData(AdvEngine engine, AdvSaveData saveData)
		{
			Debug.LogError($"{nameof(AdvSaveManagerAsync)}使用時に同期の{nameof(WriteSaveData)}()が呼ばれました。"
				+ $"呼び出し元を非同期経路（{nameof(WriteSaveDataAsync)}）に対応させてください。", this);
		}

		public override bool ReadAutoSaveData()
		{
			Debug.LogError($"{nameof(AdvSaveManagerAsync)}使用時に同期の{nameof(ReadAutoSaveData)}()が呼ばれました。"
				+ $"呼び出し元を非同期経路（{nameof(ReadSaveDataAsync)}）に対応させてください。", this);
			return false;
		}

		public override bool ReadQuickSaveData()
		{
			Debug.LogError($"{nameof(AdvSaveManagerAsync)}使用時に同期の{nameof(ReadQuickSaveData)}()が呼ばれました。"
				+ $"呼び出し元を非同期経路（{nameof(ReadSaveDataAsync)}）に対応させてください。", this);
			return false;
		}

		public override void ReadAllSaveData()
		{
			Debug.LogError($"{nameof(AdvSaveManagerAsync)}使用時に同期の{nameof(ReadAllSaveData)}()が呼ばれました。"
				+ $"呼び出し元を非同期経路（{nameof(ReadAllSaveDataAsync)}）に対応させてください。", this);
		}

		public override void DeleteSaveData(AdvSaveData saveData)
		{
			Debug.LogError($"{nameof(AdvSaveManagerAsync)}使用時に同期の{nameof(DeleteSaveData)}()が呼ばれました。"
				+ $"呼び出し元を非同期経路（{nameof(DeleteSaveDataAsync)}）に対応させてください。", this);
		}

		public override void DeleteAllSaveData()
		{
			Debug.LogError($"{nameof(AdvSaveManagerAsync)}使用時に同期の{nameof(DeleteAllSaveData)}()が呼ばれました。"
				+ $"呼び出し元を非同期経路（{nameof(DeleteAllSaveDataAndSystemDataAsync)}）に対応させてください。", this);
		}

		//終了時・ポーズ時のオートセーブは、AdvAutoSaveController側の責務とし、ここでは何もしない
		protected override void OnApplicationQuit()
		{
		}
		protected override void OnApplicationPause(bool pauseStatus)
		{
		}

		//指定したセーブデータを非同期で読み込む
		public async Awaitable<bool> ReadSaveDataAsync(AdvSaveData saveData, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return await ReadSaveDataCoreAsync(saveData, cancellationToken);
		}

		//オートセーブ・クイックセーブ・通常セーブの全スロットを非同期で読み込む
		public async Awaitable ReadAllSaveDataAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (AsyncFileIOQueue == null)
			{
				throw new InvalidOperationException($"{nameof(AdvSaveManagerAsync)} requires {nameof(AsyncFileIOQueue)}");
			}
			var targets = new List<AdvSaveData>();
			if (IsAutoSave) targets.Add(AutoSaveData);
			targets.Add(QuickSaveData);
			targets.AddRange(SaveDataList);

			//1回のReadFilesAsyncにまとめて呼ぶ（1コミットで完結させるため）
			IReadOnlyList<AsyncFileEntry> files = await AsyncFileIOQueue.ReadFilesAsync(
				targets.ConvertAll(d => d.Path), cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();

			//ReadFilesAsyncの実装が入力と同じ順序で返すとは限らないため、Pathで対応付ける
			var filesByPath = new Dictionary<string, AsyncFileEntry>(files.Count);
			foreach (AsyncFileEntry file in files) filesByPath[file.Path] = file;

			foreach (AdvSaveData saveData in targets)
			{
				ApplyReadEntry(saveData, filesByPath[saveData.Path]);
			}
		}

		//指定したセーブデータを非同期で書き込む
		public async Awaitable WriteSaveDataAsync(AdvEngine engine, AdvSaveData saveData, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			//バイナリを作るまでは継承元の同期ロジックをそのまま使う（PrepareWriteSaveData参照）
			if (!PrepareWriteSaveData(engine, saveData)) return;

			if (AsyncFileIOQueue == null)
			{
				throw new InvalidOperationException($"{nameof(AdvSaveManagerAsync)} requires {nameof(AsyncFileIOQueue)}");
			}

			//システムセーブデータの書き込みも合わせて非同期で行う（1回にまとめてアトミックにする）。
			//
			//明示的操作はバイナリ化をキューに入る前に即座に行う（dequeue時点で遅延させると、待機中に
			//同じsaveDataインスタンスへ別の書き込みが割り込みズレが起こる。これはオートセーブの
			//「dequeue時点の最新状態を書く」設計とは逆の要求のため区別している）
			var files = new List<AsyncFileEntry>(2);
			if (engine.SystemSaveData.IsAutoSaveOnNormalSave
				&& engine.SystemSaveData is IAdvSystemSaveDataAsync sysExt)
			{
				//BuildWriteFileEntryは書き込み対象でなければnullを返す（DontUseSystemSaveData等）
				AsyncFileEntry systemEntry = sysExt.BuildWriteFileEntry();
				if (systemEntry != null) files.Add(systemEntry);
			}
			files.Add(BuildWriteFileEntry(saveData));

			await AsyncFileIOQueue.WriteFilesAsync(() => files, AsyncFileIOPriority.Explicit, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
		}

		//オートセーブ対象があるか
		public bool HasAutoSaveTarget => IsAutoSave && CurrentAutoSaveData != null && CurrentAutoSaveData.IsSaved;

		//オートセーブ用の書き込みエントリを生成する
		public AsyncFileEntry BuildAutoSaveFileEntry()
		{
			if (!HasAutoSaveTarget) return null;
			return BuildWriteFileEntry(CurrentAutoSaveData);
		}

		//指定したセーブデータを非同期で削除する
		public async Awaitable DeleteSaveDataAsync(AdvSaveData saveData, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (AsyncFileIOQueue == null)
			{
				throw new InvalidOperationException($"{nameof(AdvSaveManagerAsync)} requires {nameof(AsyncFileIOQueue)}");
			}
			//事前の存在確認は二重処理を生むため行わずDeleteFilesAsyncへ委ねる（対象不在でも無害）
			await AsyncFileIOQueue.DeleteFilesAsync(new[] { saveData.Path }, cancellationToken);
			saveData.Clear();
		}

		//削除対象の全AdvSaveData（オート・クイック・通常N枠）を1つのリストにまとめる
		List<AdvSaveData> AllSaveDataList()
		{
			var list = new List<AdvSaveData>(2 + SaveDataList.Count) { AutoSaveData, QuickSaveData };
			list.AddRange(SaveDataList);
			return list;
		}

		/// <summary>
		/// 全セーブデータ（オート・クイック・通常N枠）とシステムセーブデータをまとめて1コミットで削除する
		/// 「全削除して終了」のような、システムセーブデータごと消したい場面専用
		/// </summary>
		public async Awaitable DeleteAllSaveDataAndSystemDataAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (AsyncFileIOQueue == null)
			{
				throw new InvalidOperationException($"{nameof(AdvSaveManagerAsync)} requires {nameof(AsyncFileIOQueue)}");
			}
			List<AdvSaveData> targets = AllSaveDataList();
			List<string> paths = targets.ConvertAll(d => d.Path);

			//システムセーブデータのパスも追加
			//IAdvSystemSaveDataAsyncはAwakeのチェック通り通常は存在するが、無ければ対象から除く
			bool hasSystemSaveData = this.TryGetComponent(out AdvSystemSaveData systemSaveData)
			                         && systemSaveData is IAdvSystemSaveDataAsync;
			if (hasSystemSaveData)
			{
				paths.Add(systemSaveData.Path);
			}
			await AsyncFileIOQueue.DeleteFilesAsync(paths, cancellationToken);
			foreach (AdvSaveData saveData in targets) saveData.Clear();
		}

		//単一セーブデータの読み込み実処理（ReadSaveDataAsyncの内部実装）
		async Awaitable<bool> ReadSaveDataCoreAsync(AdvSaveData saveData, CancellationToken cancellationToken)
		{
			if (AsyncFileIOQueue == null)
			{
				throw new InvalidOperationException($"{nameof(AdvSaveManagerAsync)} requires {nameof(AsyncFileIOQueue)}");
			}
			IReadOnlyList<AsyncFileEntry> files = await AsyncFileIOQueue.ReadFilesAsync(new[] { saveData.Path }, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			return ApplyReadEntry(saveData, files[0]);
		}

		//読み込んだAsyncFileEntryをAdvSaveDataへ反映する（単一読み込み・一括読み込み共通）
		bool ApplyReadEntry(AdvSaveData saveData, AsyncFileEntry entry)
		{
			if (!entry.Exists)
			{
				//ファイルが無い＝未セーブをIsSavedに反映（残っていると削除後の再読み込みで古いまま残る）
				saveData.Clear();
				return false;
			}
			byte[] decoded = FileIOManager.Decode(entry.Bytes);
			using (var stream = new MemoryStream(decoded))
			using (var reader = new BinaryReader(stream))
			{
				saveData.Read(reader);
			}
			return true;
		}

		//書き込み用のバイナリエントリを生成する
		AsyncFileEntry BuildWriteFileEntry(AdvSaveData saveData)
		{
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream))
			{
				saveData.Write(writer);
				byte[] encoded = FileIOManager.Encode(stream.ToArray());
				return new AsyncFileEntry(saveData.Path, encoded);
			}
		}
	}
}
