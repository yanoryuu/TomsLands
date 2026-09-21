// UTAGE: Unity Text Adventure Game Engine (c) Ryohei Tokimura
using System;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace UtageExtensions
{
	//コルーチンからAwaitableの完了を待つための汎用ブリッジ。
	//既存のコルーチンチェーン（yield return）からAwaitableベースの非同期メソッドを呼びたい場合に使う。
	public static class UtageExtensionMethodsAwaitable
	{
		//戻り値を持つAwaitable<T>をコルーチンで使うための拡張メソッド。
		//onResultは省略すると結果を捨てる。
		//例外はコルーチンに伝播して、コルーチンが止まる
		//補足：戻り値の無いAwaitableはIEnumerator実装のため直接yield returnできる
		public static IEnumerator ToCoroutine<T>(this Awaitable<T> awaitable, Action<T> onResult = null)
		{
			var awaiter = awaitable.GetAwaiter();
			while (!awaiter.IsCompleted) yield return null;
			var result = awaiter.GetResult();
			onResult?.Invoke(result);
		}

		//例外処理のできるAwaitableのコルーチンブリッジ。例外を検知した場合はonExceptionに渡す。
		//例外を検知してもコルーチンを止めない（再送出しない）。
		//意図的に例外を握りつぶして後続処理を続けたい場合にのみ使うこと。
		public static IEnumerator ToCoroutineCatchException(this Awaitable awaitable, Action<Exception> onException)
		{
			var awaiter = awaitable.GetAwaiter();
			while (!awaiter.IsCompleted) yield return null;
			try
			{
				awaiter.GetResult();
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				onException(e);
			}
		}

		public static IEnumerator ToCoroutineCatchException<T>(this Awaitable<T> awaitable, Action<Exception> onException, Action<T> onResult = null)
		{
			var awaiter = awaitable.GetAwaiter();
			while (!awaiter.IsCompleted) yield return null;
			try
			{
				var result = awaiter.GetResult();
				onResult?.Invoke(result);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				onException(e);
			}
		}

		//コルーチンのWaitUntilに相当する、条件成立まで待つ非同期版。
		//conditionがtrueになるまで1フレームずつ待つ（Unity公式マニュアル記載のパターン）。
		public static async Awaitable UntilAsync(Func<bool> condition, CancellationToken cancellationToken)
		{
			while (!condition())
			{
				//NextFrameAsyncには直接CancellationTokenを持たせない（Alloc回避）
				await Awaitable.NextFrameAsync();
				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		//Awaitableをfire-and-forgetで実行する。
		//非同期処理の起動点の処理を集約する意味で作成。
		//（例: UIイベントハンドラのようなvoidメソッドからasync Awaitableの世界へ入る入口）
		//キャンセルは正常系として扱い、それ以外の例外はonExceptionへ（無ければDebug.LogException）。
		//何かが完了を待つ必要がある場合はここを経由せず、awaitまたはToCoroutine()で素直に待つこと。
		public static async void FireAndForget(this Awaitable awaitable, Action<Exception> onException = null)
		{
			try
			{
				await awaitable;
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				if (onException != null) onException(e);
				else Debug.LogException(e);
			}
		}
	}
}
