// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System;
using UnityEngine;

namespace Utage
{
	/// <summary>
	/// IAdvSaveExceptionHandlerの実装例。セーブ関連の例外発生時にガイドメッセージを表示し、
	/// 自分と同じGameObjectがUguiView（画面）であればその画面を閉じて戻る、という書き方の例。
	/// アタッチ先ごとに別インスタンスとして使う（アタッチ先などはWebドキュメント参照
	/// https://madnesslabo.net/utage/?page_id=16100 ）。
	/// ここでは1コンポーネントを場合分けしたりしてるが、実際には実装したい処理ごとに独自のコンポーネントを作るのが望ましい。
	/// </summary>
	[AddComponentMenu("Utage/ADV/Examples/SampleSaveExceptionHandler")]
	public class SampleSaveExceptionHandler : MonoBehaviour, IAdvSaveExceptionHandler
	{
		//ガイドメッセージの表示。設定してないときは表示しない
		[SerializeField] SystemUiGuideMessage guideMessage;

		public void OnSaveDataException(AdvSaveOperationType operation, Exception e)
		{
			//ログを残す場合
			Debug.LogException(e, this);

			//操作種別に応じたガイドメッセージを表示する例。設定してないときは表示しない
			SystemText? messageKey = ToGuideMessageKey(operation);
			if (guideMessage != null && messageKey != null)
			{
				guideMessage.Open(LanguageSystemText.LocalizeText(messageKey.Value));
			}

			//画面を閉じて戻るなど、UI操作例
			//一覧画面オープン（OpenSaveLoadList）以外は、
			//元がUguiView画面ならそれを閉じる処理
			if (operation != AdvSaveOperationType.OpenSaveLoadList
				&& this.TryGetComponent(out UguiView view))
			{
				view.Close();
			}
			
			//例外スローの書き方の例
			//基本的には例外は再スローしなくていいが、あえて再スローして上位へ伝播させたい場合
			//元のスタックトレースを保ったまま投げ直すなら
			//throw e;ではなくExceptionDispatchInfo.Capture(e).Throw()を使うこと
			//System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw();
		}

		//操作種別に応じたガイドメッセージを選ぶ。
		static SystemText? ToGuideMessageKey(AdvSaveOperationType operation)
		{
			switch (operation)
			{
				case AdvSaveOperationType.OpenSaveLoadList: return SystemText.UtageGuideMessageSaveDataLoadFailed;
				case AdvSaveOperationType.Save: return SystemText.UtageGuideMessageSaveFailed;
				case AdvSaveOperationType.QuickSave: return SystemText.UtageGuideMessageQuickSaveFailed;
				case AdvSaveOperationType.QuickLoad: return SystemText.UtageGuideMessageQuickLoadFailed;
				//他は未対応
				default: return null;
			}
		}
	}
}
