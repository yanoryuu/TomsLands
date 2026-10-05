// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System;
using UnityEngine;
using UtageExtensions;

namespace Utage
{
	public class SampleCustomSaveLoadButton : MonoBehaviour
	{
		//セーブロード画面の制御コンポーネント
		UtageUguiSaveLoad SaveLoad => this.GetComponentCacheInParent(ref saveLoad);
		UtageUguiSaveLoad saveLoad;

		AdvEngine Engine => SaveLoad.Engine;

		UtageUguiSaveLoadItem SaveLoadItem => this.GetComponentCache(ref saveLoadItem);
		[SerializeField] UtageUguiSaveLoadItem saveLoadItem;

		//セーブデータの削除
		public void OnClickDelete()
		{
			if (Engine.SaveManager is IAdvSaveManagerAsync asyncExtension)
			{
				DeleteAndRefreshAsync(asyncExtension).FireAndForget();
			}
			else
			{
				//セーブデータの削除
				Engine.SaveManager.DeleteSaveData(SaveLoadItem.Data);
				//セーブボタンの表示の更新
				SaveLoadItem.Refresh(SaveLoad.IsSave);
			}
		}

		//削除完了を待ってから表示を更新
		async Awaitable DeleteAndRefreshAsync(IAdvSaveManagerAsync asyncExtension)
		{
			try
			{
				//このコンポーネントが破壊されても、削除処理を止めないように
				//Engine.destroyCancellationTokenを使う
				await asyncExtension.DeleteSaveDataAsync(SaveLoadItem.Data, Engine.destroyCancellationToken);
				if (SaveLoadItem == null) return;
				SaveLoadItem.Refresh(SaveLoad.IsSave);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception)
			{
				//削除失敗時の例外処理はここに書く
				throw;
			}
		}
	}
}
