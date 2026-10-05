// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Utage
{
	/// <summary>
	/// 実ファイルI/O（非同期）の拡張用インターフェース。
	/// FileIOManagerとは違い、バイナリの生成・符号化は行わず、読み書きだけを行う。
	/// FileIOManagerと同じGameObjectに独立コンポーネントとしてアタッチし、
	/// AsyncFileIOQueue経由で呼ぶ（直接呼ばない）。
	/// バッチ単位API（AsyncFileEntry参照）で、Commit等のライフサイクル管理は実装に隠蔽する。
	/// </summary>
	public interface IAsyncFileIO
	{
		/// <summary>
		/// 指定したパスのファイルをまとめて読み込む（符号化されたままのバイト列、デコードは呼び出し元）。
		/// （事前の存在確認は二重処理を生むため）ファイル不存在は例外を投げず<see cref="AsyncFileEntry.NotFound"/>を返す
		/// それ以外のI/Oエラーは例外をスローすること
		/// </summary>
		Awaitable<IReadOnlyList<AsyncFileEntry>> ReadFilesAsync(
			IReadOnlyCollection<string> paths, CancellationToken cancellationToken);

		/// <summary>
		/// 複数ファイルをまとめて書き込む。
		/// </summary>
		Awaitable WriteFilesAsync(IReadOnlyList<AsyncFileEntry> files, CancellationToken cancellationToken);

		/// <summary>
		/// 複数ファイルをまとめて削除する。存在しないファイルの削除は何もしない。
		/// </summary>
		Awaitable DeleteFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);
	}
}
