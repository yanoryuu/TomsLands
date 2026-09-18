// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
namespace Utage
{
	/// <summary>
	/// IAsyncFileIOが扱う1ファイル分の情報
	/// </summary>
	public class AsyncFileEntry
	{
		public string Path { get; }
		public byte[] Bytes { get; }

		/// <summary>
		/// ReadFilesAsync専用。falseなら対象パスが存在しなかったことを表す（Bytesはnull）。
		/// 書き込み用は常にtrue
		/// </summary>
		public bool Exists { get; }

		public AsyncFileEntry(string path, byte[] bytes)
		{
			Path = path;
			Bytes = bytes;
			Exists = true;
		}

		AsyncFileEntry(string path)
		{
			Path = path;
			Bytes = null;
			Exists = false;
		}

		/// <summary>
		/// ReadFilesAsyncの実装が、対象パスにファイルが存在しなかった場合に使う
		/// </summary>
		public static AsyncFileEntry NotFound(string path) => new AsyncFileEntry(path);
	}
}
