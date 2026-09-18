// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System;
using UnityEngine;

namespace Utage
{
	/// <summary>
	/// セーブ関連の非同期処理の種別（IAdvSaveExceptionHandlerへの通知に使う）
	/// </summary>
	public enum AdvSaveOperationType
	{
		OpenSaveLoadList,	//セーブ/ロード一覧画面のオープン（読み込み）
		Save,				//明示的なセーブ操作
		QuickSave,			//クイックセーブ
		QuickLoad,			//クイックロード
		AutoSave,			//オートセーブ（AdvAutoSaveController、一定間隔/改ページ/Pause/Quitトリガー）
		WriteSystemData,	//システムセーブデータの暗黙の自動書き込み（設定画面を閉じた時・シナリオ実行中の自動保存）
		Delete,				//セーブデータの削除（1件）
		DeleteAllAndQuit,	//全セーブデータ削除＋終了（デバッグメニュー等）
	}

	/// <summary>
	/// セーブ関連の非同期処理で（キャンセルを除く）例外が発生した時の例外処理を独自実装するためのハンドラ。
	/// このインターフェースを継承したコンポーネントを、呼び出し元と同じGameObjectにアタッチすると、
	/// デフォルトの例外処理（ガイドメッセージ表示・ログ出力等）の代わりにインターフェースの実装が呼ばれる。
	/// アタッチ先のGameObjectなどはWebドキュメントを参照。
	/// https://madnesslabo.net/utage/?page_id=16100
	/// 実装例は、サンプルコードSampleSaveExceptionHandler.csを参照。
	/// </summary>
	public interface IAdvSaveExceptionHandler
	{
		void OnSaveDataException(AdvSaveOperationType operation, Exception e);
	}

	/// <summary>
	/// システムセーブデータ読み込み失敗時（起動シーケンス中）、リトライするかどうかを独自実装
	/// するためのハンドラ（`IAdvSaveExceptionHandler`とは別の専用インターフェース）。
	/// AdvEngineと同じGameObjectにアタッチすると、
	/// デフォルトの処理の代わりにインターフェースの実装が呼ばれる。
	/// </summary>
	public interface IAdvSystemSaveDataRetryHandler
	{
		/// <returns>trueならリトライする。falseなら諦めて例外を呼び出し元へ伝播させる</returns>
		Awaitable<bool> OnSystemSaveDataReadFailedAsync(Exception e);
	}
}
