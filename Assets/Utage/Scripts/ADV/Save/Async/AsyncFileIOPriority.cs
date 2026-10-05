// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
namespace Utage
{
	/// <summary>
	/// AsyncFileIOQueueにおける要求の優先度。
	/// 既に実行中の要求には影響せず、次に何を実行するかの優先度を決めるだけ。
	/// Explicitの要求が来た場合、AutoSaveの要求は後回しになる。
	/// </summary>
	public enum AsyncFileIOPriority
	{
		/// <summary>クイック/通常セーブ・ロード・一覧取得等、ユーザー操作起点の明示的な要求</summary>
		Explicit,

		/// <summary>改ページ・インターバル・Pause等、オートセーブトリガー起点の要求</summary>
		AutoSave,
	}
}
