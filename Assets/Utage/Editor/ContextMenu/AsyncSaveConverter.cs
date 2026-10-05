// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using UnityEditor;
using UnityEngine;
using UtageExtensions;

namespace Utage
{
	//シーン上のAdvSaveManager/AdvSystemSaveDataを
	//非同期対応版（AdvSaveManagerAsync/AdvSystemSaveDataAsync）に差し替え、
	//AdvAutoSaveControllerを追加するエディタ拡張。
	//FileIOManager自体は差し替えない。IAsyncFileIO実装コンポーネント（例: SampleAsyncFileIO）は
	//プロジェクトごとに適したものを選ぶ必要があるため、このツールでは追加しない（手動AddComponent）。
	public static class AsyncSaveConverter
	{
		const string MenuPath = "GameObject/Utage/Convert To Async Save";

		[MenuItem(MenuPath, false)]
		public static void ConvertToAsyncSave()
		{
			var go = Selection.activeGameObject;
			if (go == null)
			{
				Debug.LogError("変換対象のGameObjectを選択してください");
				return;
			}

			using (new EditorUndoGroupScope(nameof(ConvertToAsyncSave)))
			{
				//変換前に対象コンポーネントの有無を控えておく。
				bool hasSaveManager = go.GetComponent<AdvSaveManager>() != null;
				bool hasSystemSaveData = go.GetComponent<AdvSystemSaveData>() != null;

				//古いコンポーネントがあったら新しいコンポーネントに置き換え、シリアライズ値を引き継ぐ。
				ReplaceComponentKeepingValues<AdvSaveManager, AdvSaveManagerAsync>(go);
				ReplaceComponentKeepingValues<AdvSystemSaveData, AdvSystemSaveDataAsync>(go);

				if (go.GetComponent<AdvAutoSaveController>() == null)
				{
					Undo.AddComponent<AdvAutoSaveController>(go);
				}

				if (hasSaveManager || hasSystemSaveData)
				{
					//FileIOManagerはAdvSaveManagerと同じGameObjectとは限らない
					//（シーン内から検索して見つかったものが使われる。例: Managers/FileManager）ため、
					//実際にIAsyncFileIOを追加すべきGameObjectを具体的に案内する
					var fileIOManager = WrapperFindObject.FindObjectOfType<FileIOManager>();
					string fileIOManagerPath = fileIOManager != null
						? fileIOManager.gameObject.GetHierarchyPath()
						: "(FileIOManagerがシーン内に見つかりませんでした)";

					//AsyncFileIOQueueはプロジェクト固有の選択が不要な共通フレームワークコードのため、
					//IAsyncFileIOの実装（プロジェクトごとに選ぶもの）とは異なりここで自動追加する
					if (fileIOManager != null && fileIOManager.GetComponent<AsyncFileIOQueue>() == null)
					{
						Undo.AddComponent<AsyncFileIOQueue>(fileIOManager.gameObject);
					}

					Debug.Log($"{go.name}: 非同期セーブ対応への変換が完了しました。"
						+ $"IAsyncFileIO実装コンポーネント（例: SampleAsyncFileIO）は自動追加されないため、"
						+ $"FileIOManagerと同じGameObject（{fileIOManagerPath}）に手動でAddComponentしてください"
						+ "（AsyncFileIOQueueは自動追加済みです）。",
						fileIOManager != null ? (Object)fileIOManager : go);
				}
				else
				{
					Debug.LogWarning($"{go.name}にAdvSaveManager/AdvSystemSaveDataが見つかりませんでした", go);
				}
			}
		}

		[MenuItem(MenuPath, true)]
		public static bool ValidateConvertToAsyncSave()
		{
			var go = Selection.activeGameObject;
			return go != null
				&& (go.GetComponent<AdvSaveManager>() != null || go.GetComponent<AdvSystemSaveData>() != null);
		}

		//TOldコンポーネントをTNewに差し替え、SerializedObject経由でシリアライズ値を引き継ぐ。
		//ComponentUtility.CopyComponent/PasteComponentValuesは型が完全一致しないと機能しない
		//（基底クラス→派生クラスへは値が引き継がれない）ため使わない。
		static void ReplaceComponentKeepingValues<TOld, TNew>(GameObject go)
			where TOld : Component
			where TNew : TOld
		{
			var old = go.GetComponent<TOld>();
			if (old == null) return;
			if (old is TNew) return; //既に変換済み

			var newComp = Undo.AddComponent<TNew>(go);
			CopySerializedValues(old, newComp);
			Undo.DestroyObjectImmediate(old);
		}

		static void CopySerializedValues(Component from, Component to)
		{
			var fromSo = new SerializedObject(from);
			var toSo = new SerializedObject(to);
			var prop = fromSo.GetIterator();
			bool enterChildren = true;
			while (prop.NextVisible(enterChildren))
			{
				enterChildren = false;
				if (prop.propertyPath == "m_Script") continue;
				var target = toSo.FindProperty(prop.propertyPath);
				if (target != null)
				{
					toSo.CopyFromSerializedProperty(prop);
				}
			}
			toSo.ApplyModifiedProperties();
		}
	}
}
