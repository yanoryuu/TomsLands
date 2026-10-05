// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UtageExtensions;

namespace Utage
{

	/// <summary>
	/// ゲームで共通して使うシステムセーブデータ
	/// </summary>
	[AddComponentMenu("Utage/ADV/Internal/AdvSystemSaveData")]
	public class AdvSystemSaveData : MonoBehaviour
		,IAdvSaveDelete
	{
		/// <summary>
		/// システムセーブデータを使わない
		/// </summary>
		public bool DontUseSystemSaveData 
		{
			get => dontUseSystemSaveData;
			set => dontUseSystemSaveData = value;
		}
		[SerializeField]
		bool dontUseSystemSaveData = false;
		
		//アプリ終了時やスリープ時にオートセーブするか
		public bool IsAutoSaveOnQuit
		{
			get => isAutoSaveOnQuit;
			set => isAutoSaveOnQuit = value;
		}
		[SerializeField]
		bool isAutoSaveOnQuit = true;

		//通常のセーブデータ書き込み時にシステムセーブデータも書き込むか
		public bool IsAutoSaveOnNormalSave
		{
			get => isAutoSaveOnNormalSave;
			set => isAutoSaveOnNormalSave = value;
		}
		[SerializeField] bool isAutoSaveOnNormalSave = true;

		//BgEventCommand使用時にオートセーブするか（CGギャラリーの解放を確実にセーブ）
		public bool IsAutoSaveBgEventCommand
		{ 
			get => isAutoSaveBgEventCommand;
			set => isAutoSaveBgEventCommand = value;
		}
		[SerializeField] bool isAutoSaveBgEventCommand = false;

		protected FileIOManager FileIOManager => this.GetComponentCacheFindIfMissing( ref fileIOManager);
		[SerializeField]
		FileIOManager fileIOManager;

		
		/// <summary>
		/// ディレクトリ名
		/// </summary>
		public string DirectoryName
		{
			get { return directoryName; }
			set { directoryName = value; }
		}
		[SerializeField]
		string directoryName = "Save";

		/// <summary>
		/// ファイル名
		/// </summary>
		public string FileName
		{
			get { return fileName; }
			set { fileName = value; }
		}
		[SerializeField]
		string fileName = "system";

		/// <summary>
		/// ファイルパス
		/// </summary>
		public string Path { get; protected set; }

		/// <summary>
		/// 既読のデータ
		/// </summary>
		public AdvReadHistorySaveData ReadData { get { return this.readData; } }
		AdvReadHistorySaveData readData = new AdvReadHistorySaveData();

		/// <summary>
		/// 選択肢のデータ
		/// </summary>
		public AdvSelectedHistorySaveData SelectionData { get { return this.selectionData; } }
		AdvSelectedHistorySaveData selectionData = new AdvSelectedHistorySaveData();

		/// <summary>
		/// 回想モード用のデータ
		/// </summary>
		public AdvGallerySaveData GalleryData { get { return this.galleryData; } }
		AdvGallerySaveData galleryData = new AdvGallerySaveData();

		protected AdvEngine Engine { get { return this.engine; } }
		protected AdvEngine engine;

		/// <summary>
		/// 初期化フラグ
		/// </summary>
		protected bool isInit = false;

		/// <summary>
		/// 初期化
		/// </summary>
		public virtual void Init(AdvEngine engine)
		{
			this.engine = engine;
			if (!TryReadSaveData())
			{
				InitDefault();
			}
			isInit = true;
		}

		/// <summary>
		/// デフォルト値で初期化(初回でセーブデータがない場合)
		/// </summary>
		protected virtual void InitDefault()
		{
			this.Engine.Config.InitDefault();
		}

		/// <summary>
		/// 読み込み失敗時のフォールバック（デフォルト値で初期化）。
		/// 非同期拡張時、起動シーケンス側（AdvEngine.CoBootInit）がInitAsyncの例外を
		/// キャッチした際に呼ぶ（InitDefaultはprotectedのため公開する形で用意している）
		/// </summary>
		public virtual void InitFallback()
		{
			InitDefault();
			isInit = true;
		}

		//セーブデータのディレクトリ作成とPathの確定（読み込みの前処理）
		protected virtual void EnsureSaveDirAndPath()
		{
			string saveDir = FilePathUtil.Combine(FileIOManager.SdkPersistentDataPath, DirectoryName);
			//セーブデータのディレクトリがなければ作成。
			FileIOManager.CreateDirectory(saveDir);
			Path = FilePathUtil.Combine(saveDir, FileName);
		}

		protected virtual bool TryReadSaveData()
		{
			if (DontUseSystemSaveData) return false;

			EnsureSaveDirAndPath();
			if (!FileIOManager.Exists(Path)) return false;

			return FileIOManager.ReadBinaryDecode(Path, ReadBinary);
		}

		/// <summary>
		/// 書き込み
		/// </summary>
		public virtual void Write()
		{
			if (!DontUseSystemSaveData && isInit)
			{
				FileIOManager.WriteBinaryEncode(Path, WriteBinary);
			}
		}

		
		//通常のセーブデータ書き込み時に呼ばれる処理
		public virtual void OnWriteNormalSaveData()
		{
			if (!IsAutoSaveOnNormalSave) return;
			//システムセーブデータも書き込む
			Write();
		}

		//セーブデータを消去して終了(インターフェースを使用するのでpublicに変更)
		public virtual void OnDeleteAllSaveDataAndQuit()
		{
			Delete();
			isAutoSaveOnQuit = false;
		}

		/// <summary>
		/// セーブデータを消去
		/// </summary>
		public virtual void Delete()
		{
			FileIOManager.Delete(Path);
		}

		//ゲーム終了時
		protected virtual void OnApplicationQuit()
		{
			AutoSave();
		}

		//アプリがポーズしたとき
		protected virtual void OnApplicationPause(bool pauseStatus)
		{
			if (pauseStatus)
			{
				AutoSave();
			}
		}

		protected virtual void AutoSave()
		{
			//オートセーブ
			if (IsAutoSaveOnQuit)
			{
				Write();
			}
		}


		static protected readonly int MagicID = FileIOManager.ToMagicID('S', 'y', 's', 't');  //識別ID
		protected const int Version = 4;  //最新ファイルバージョン

		protected virtual List<IBinaryIO> DataList
		{
			get
			{
				List<IBinaryIO> list = new List<IBinaryIO>()
				{
					ReadData,					//既読データ
					SelectionData,				//選択肢の選択済みデータ
					Engine.Config,				//コンフィグのデータ
					GalleryData,				//ギャラリーデータ
					Engine.Param.SystemData,	//システムセーブデータにセーブするパラメーターのデータ
				};
				//拡張用
				list.AddRange(Engine.GetComponentsInChildren<IAdvSystemSaveDataCustom>(true));
				return list;
			}
		}

		//バイナリ読み込み
		protected virtual void ReadBinary(BinaryReader reader)
		{
			int magicID = reader.ReadInt32();
			if (magicID != MagicID)
			{
				throw new System.Exception("Read File Id Error");
			}

			int fileVersion = reader.ReadInt32();
			if (fileVersion == Version)
			{
				BinaryBuffer.Read(reader, DataList);
			}
			else
			{
				throw new System.Exception(LanguageErrorMsg.LocalizeTextFormat(ErrorMsg.UnknownVersion, fileVersion));
			}
		}

		//バイナリ書き込み
		protected virtual void WriteBinary(BinaryWriter writer)
		{
			writer.Write(MagicID);                  //識別ID
			writer.Write(Version);                  //バージョン

			BinaryBuffer.Write(writer, DataList);
		}
	}
}
