// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using UnityEngine;
using UtageExtensions;

namespace Utage
{

	/// <summary>
	/// コマンド：背景表示・切り替え
	/// </summary>
	public class AdvCommandBgEvent : AdvCommandBgBase
	{
		//立ち絵表示をしないモードにするかどうか
		bool IsEventMode { get; }
		
		public AdvCommandBgEvent(StringGridRow row, AdvSettingDataManager dataManager)
			: base(row, dataManager)
		{
			IsEventMode = this.ParseCellOptional(AdvColumnName.Arg2, true);
		}

		public override void DoCommand(AdvEngine engine)
		{
			//CGギャラリーの解放
			var systemSaveData = engine.SystemSaveData;
			bool isNewCg = systemSaveData.GalleryData.TryAddCgLabel(label);
			//コマンド実行時のオートセーブが有効、かつ実際に新規CGが解放された場合のみオートセーブ
			//（既に解放済みのCGの再表示では変化が無いため無駄な書き込みをしない）
			if (isNewCg && systemSaveData.IsAutoSaveBgEventCommand)
			{
				if (systemSaveData is IAdvSystemSaveDataAsync)
				{
					//ここはシナリオ進行を止めてはいけない場所のため、
					//待たずにバックグラウンドで自動セーブするFireAndForget
					//AutoWriteSystemDataAsyncは自動セーブ用に例外を内部で処理する設計
					engine.AutoWriteSystemDataAsync(engine.destroyCancellationToken).FireAndForget();
				}
				else
				{
					systemSaveData.Write();
				}
			}
			engine.GraphicManager.IsEventMode = IsEventMode;
			
			//表示する
			AdvGraphicOperationArg graphicOperationArg = DoCommandBgSub(engine);
			//キャラクターは非表示にする
			if (IsEventMode)
			{
				engine.GraphicManager.CharacterManager.FadeOutAll(graphicOperationArg.GetSkippedFadeTime(engine));
			}
		}
	}
}
