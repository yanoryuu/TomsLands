// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System.Threading;
using UnityEngine;

namespace Utage
{
	/// <summary>
	/// AdvSystemSaveDataの非同期拡張用インターフェース。
	/// AdvSystemSaveDataを継承したサブクラスが実装する想定（既存コンポーネントの代わりにアタッチする）。
	/// 呼び出し元は`SystemSaveData is IAdvSystemSaveDataAsync`で拡張の有無を判定する。
	/// 例外は握りつぶさず伝播させる。
	/// </summary>
	public interface IAdvSystemSaveDataAsync
	{
		/// <summary>
		/// 初期化。ファイル読み込みだけを非同期で行う。
		/// </summary>
		Awaitable InitAsync(AdvEngine engine, CancellationToken cancellationToken);

		/// <summary>
		/// システムセーブデータを非同期で書き込む
		/// </summary>
		Awaitable WriteAsync(AsyncFileIOPriority priority, CancellationToken cancellationToken);

		/// <summary>
		/// 実ファイルI/Oを行わず、書き込み用のバイナリだけを生成する。
		/// 通常セーブと同時にコミットしたい場合など、複数のAsyncFileEntryをまとめて渡すために使う。
		/// 書き込み対象でない場合（DontUseSystemSaveDataがtrue、または未初期化）はnullを返す
		/// </summary>
		AsyncFileEntry BuildWriteFileEntry();

		/// <summary>
		/// BuildWriteFileEntryを実際に呼ばず、生成するかどうかを安価に判定する
		/// （事前チェック用。Build自体はシリアライズ・エンコードまで走り無駄になるため）
		/// </summary>
		bool HasWriteTarget { get; }
	}
}
