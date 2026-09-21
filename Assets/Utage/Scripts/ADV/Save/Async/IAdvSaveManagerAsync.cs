// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System.Threading;
using UnityEngine;

namespace Utage
{
	/// <summary>
	/// AdvSaveManagerの非同期拡張用インターフェース。AdvSaveManagerを継承したサブクラスが実装する想定
	/// （既存コンポーネントの代わりにアタッチする）。
	/// 呼び出し元は`SaveManager is IAdvSaveManagerAsync`で拡張の有無を判定する。例外は握りつぶさず伝播させる。
	/// </summary>
	public interface IAdvSaveManagerAsync
	{
		/// <summary>
		/// 指定したセーブデータを非同期で読み込む（成否は完了後にsaveData.IsSavedで判定）
		/// </summary>
		Awaitable<bool> ReadSaveDataAsync(AdvSaveData saveData, CancellationToken cancellationToken);

		/// <summary>
		/// オートセーブ・クイックセーブ・通常セーブの全スロットを非同期で読み込む
		/// </summary>
		Awaitable ReadAllSaveDataAsync(CancellationToken cancellationToken);

		/// <summary>
		/// 指定したセーブデータを非同期で書き込む
		/// </summary>
		Awaitable WriteSaveDataAsync(AdvEngine engine, AdvSaveData saveData, CancellationToken cancellationToken);

		/// <summary>
		/// 指定したセーブデータを非同期で削除する
		/// </summary>
		Awaitable DeleteSaveDataAsync(AdvSaveData saveData, CancellationToken cancellationToken);

		/// <summary>
		/// 全セーブデータ（オート・クイック・通常N枠）とシステムセーブデータをまとめて非同期で削除する
		/// 「全削除して終了」のようなシステムセーブデータごと消したい場面専用
		/// </summary>
		Awaitable DeleteAllSaveDataAndSystemDataAsync(CancellationToken cancellationToken);

		/// <summary>
		/// 実ファイルI/Oのキュー。複数の拡張コンポーネントの書き込みを1回のWriteFilesAsync呼び出しに
		/// まとめてコミットしたい場合に使う
		/// </summary>
		AsyncFileIOQueue AsyncFileIOQueue { get; }

		/// <summary>
		/// オートセーブ対象があれば書き込み用エントリを生成する（実I/Oは行わない）。
		/// IsAutoSaveがfalse、またはCurrentAutoSaveDataが未セーブならnullを返す
		/// </summary>
		AsyncFileEntry BuildAutoSaveFileEntry();

		/// <summary>
		/// BuildAutoSaveFileEntryを実際に呼ばず、生成するかどうかを安価に判定する
		/// （事前チェック用。Build自体はシリアライズ・エンコードまで走り無駄になるため）
		/// </summary>
		bool HasAutoSaveTarget { get; }
	}
}
