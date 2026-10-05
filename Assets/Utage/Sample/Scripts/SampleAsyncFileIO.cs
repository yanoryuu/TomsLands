// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Utage
{
	/// <summary>
	/// 実ファイルI/O（IAsyncFileIO）の汎用サンプル実装。FileIOManagerとは継承関係を持たない
	/// 独立したコンポーネントで、既存のFileIOManager（またはその派生）と同じGameObjectに
	/// 追加でアタッチして使う（AsyncFileIOQueueも同じGameObjectに必要）。
	///
	/// Unity Platform Toolkitに接続する場合は、詳細はWebドキュメント参照
	/// https://madnesslabo.net/utage/?page_id=16100
	/// </summary>
	[AddComponentMenu("Utage/ADV/Examples/SampleAsyncFileIO")]
	public class SampleAsyncFileIO : MonoBehaviour, IAsyncFileIO
	{
		const int BufferSize = 4096;

		//AdvSaveManagerAsync/AdvSystemSaveDataAsyncが渡すPathは、Application.persistentDataPath
		//（プラットフォームによってはアクセスするだけでエラーになりうる）を含まない論理パスのため、
		//実ファイルI/Oを行うここで初めてSdkPersistentDataPathと結合しフルパス化する
		static string ToFullPath(string path) => FilePathUtil.Combine(FileIOManagerBase.SdkPersistentDataPath, path);

		/// 指定したパスのファイルをまとめて読み込む
		public async Awaitable<IReadOnlyList<AsyncFileEntry>> ReadFilesAsync(
			IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
		{
			var results = new List<AsyncFileEntry>(paths.Count);
			foreach (string path in paths)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string fullPath = ToFullPath(path);
				if (!File.Exists(fullPath))
				{
					results.Add(AsyncFileEntry.NotFound(path));
					continue;
				}
				using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
				{
					byte[] buffer = new byte[stream.Length];
					int offset = 0;
					while (offset < buffer.Length)
					{
						int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken);
						if (read <= 0) break;
						offset += read;
					}
					results.Add(new AsyncFileEntry(path, buffer));
				}
			}
			return results;
		}

		/// 指定したファイルをまとめて書き込む
		public async Awaitable WriteFilesAsync(IReadOnlyList<AsyncFileEntry> files, CancellationToken cancellationToken)
		{
			foreach (AsyncFileEntry file in files)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string fullPath = ToFullPath(file.Path);
				//ディレクトリ作成はAdvSaveManagerAsync/AdvSystemSaveDataAsync側では行わず、
				//ここ（実際にローカルファイルへ書き込む場所）の責務にしている
				string dir = Path.GetDirectoryName(fullPath);
				if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
				using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
				{
					await stream.WriteAsync(file.Bytes, 0, file.Bytes.Length, cancellationToken);
				}
			}
		}

		/// 指定したファイルをまとめて削除する
		public async Awaitable DeleteFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
		{
			foreach (string path in paths)
			{
				cancellationToken.ThrowIfCancellationRequested();
				File.Delete(ToFullPath(path));
			}
		}
	}
}
