# UniTask パターン

非同期は **UniTask**。`Task` / コルーチンを新規に増やさない（既存のコルーチンは書き換えない。挙動と実行タイミングが変わる）。

## 戻り値と起動

| 場面 | 書き方 |
|---|---|
| 待てる非同期メソッド | `async UniTask Foo(CancellationToken ct)` |
| 結果を返す | `async UniTask<T>` |
| 待たずに投げっぱなし（イベントハンドラ） | `async UniTaskVoid` + 呼び出し側で `.Forget()` |
| **`async void`** | **新規に書かない**（例外が飲まれる/捕捉できない）。既存は報告 |

```csharp
private void OnButtonClicked() => PlayAsync().Forget();

private async UniTaskVoid PlayAsync()
{
    try
    {
        await cutIn.PlayAsync(destroyCancellationToken);
    }
    catch (OperationCanceledException) { }       // キャンセルは正常終了。これだけは握ってよい
    catch (Exception e) { Debug.LogException(e); }
}
```

- `.Forget()` した処理の例外は握られず UniTask のハンドラに行く。起きうる例外は内側で try/catch して `Debug.LogException`
- キャンセル（`OperationCanceledException`）以外を空 catch で握り潰さない

## キャンセル

MonoBehaviour は `destroyCancellationToken`（Unity 2022.2+）。実コード: `cutIn.PlayAsync(destroyCancellationToken).Forget();`

```csharp
public class FooView : MonoBehaviour
{
    private async UniTaskVoid ShowAsync()
    {
        var ct = destroyCancellationToken;
        await UniTask.Delay(500, cancellationToken: ct);
        // ここに来た時点で破棄されていれば例外で抜けている
        label.text = "done";
    }
}
```

Presenter（MonoBehaviour でない）は `CancellationTokenSource` を持って `Dispose` でキャンセルする:

```csharp
private readonly CancellationTokenSource cts = new();

public void Dispose()
{
    cts.Cancel();
    cts.Dispose();
    disposables.Dispose();
}
```

- `await` の後でオブジェクトが破棄済みの可能性があるなら、トークンを渡すか `if (this == null) return;`（MonoBehaviour のみ）
- `UniTask.Delay` は既定でタイムスケールを使う。ポーズ中も進めたい演出は `DelayType.UnscaledDeltaTime`（挙動を変えないよう元の指定を維持）

## 待ち方

| 目的 | API |
|---|---|
| 時間 | `UniTask.Delay(ms, cancellationToken: ct)` |
| 1 フレーム | `UniTask.Yield(ct)` / `UniTask.NextFrame(ct)` |
| 条件 | `UniTask.WaitUntil(() => cond, cancellationToken: ct)` |
| 複数同時 | `UniTask.WhenAll(a, b)` |
| DOTween | `await tween.ToUniTask(cancellationToken: ct)` / `.AwaitForComplete()` |
| Addressables | `AddressableLoader` 経由（独自に `Addressables.LoadAssetAsync` を増やさない） |

- 待ち順（`await` の順序、`WhenAll` か直列か）を変えない。見た目のタイミングと状態変更の順序が変わる
- フェーズ遷移・Save の前後に `await` を挟まない（途中キャンセルで状態が中途半端になる）

## DOTween との併用

```csharp
await transform.DOScale(1f, 0.2f)
    .SetLink(gameObject)               // オブジェクト破棄で Tween も止める
    .ToUniTask(cancellationToken: destroyCancellationToken);
```

`SetLink` が無いと、破棄後も Tween が動き続けてエラーになる（[unity-specifics](../review/unity-specifics.md)）。

## 禁止・注意

- `async void`（新規）、`.Result` / `.Wait()` / `.GetAwaiter().GetResult()`（デッドロック）
- `Task.Run` でUnity API を触る（メインスレッド外）。`UniTask.SwitchToThreadPool` 後は `await UniTask.SwitchToMainThread()` で戻す
- `try` を使わず `.Forget()` するだけで例外が見えなくなる処理を新規に作る
- ループで `await UniTask.Yield()` するときは、毎回の割り当て（ラムダ、文字列）を避ける
- リファクタで `async void` を `UniTaskVoid` に直すのは例外処理の挙動が変わりうる。呼び出し元（UnityEvent から呼ばれていないか。`async void` は UnityEvent から直接呼べるが `UniTaskVoid` は呼べない、§0-5）を確認してから

## レビューの観点

- [ ] 新規 `async void` なし
- [ ] `.Forget()` の内側で例外処理がある
- [ ] MonoBehaviour は `destroyCancellationToken`、Presenter は CTS を `Dispose` でキャンセル
- [ ] Tween は `SetLink`
- [ ] await の順序・Save の位置が元と同じ
