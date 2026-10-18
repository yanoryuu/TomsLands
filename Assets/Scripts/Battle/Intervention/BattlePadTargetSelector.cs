using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// パッド/キー用の「狙う敵」選択（敵タップ＝CharacterView.OnMouseDown の代替）。
///
/// 開くと、生きている敵ごとに画面上の透明な選択要素（プロキシ）を敵スプライトの位置へ重ね、
/// 共通の選択枠（ControllerHighlight）をそのまま敵に出す。十字/スティックで敵を切替、
/// A で決定（タップと同じ CharacterView.OnClicked を発行）、B で閉じる。
/// マウスに持ち替えた・敵がいなくなった・配信が終わったら自動で閉じる。
/// プロキシ用の Overlay Canvas は GraphicRaycaster を持たないのでマウス操作を邪魔しない。
/// </summary>
public sealed class BattlePadTargetSelector : MonoBehaviour
{
    private const int ScopePriority = 40;
    private const float MinProxySize = 48f;

    private BattleSequencer _sequencer;
    private Func<bool> _canStay;
    private RectTransform _root;
    private UIFocusScope _scope;
    private readonly Dictionary<CharacterPresenter, Proxy> _proxies = new();
    private readonly List<Proxy> _ordered = new();
    private readonly Stack<Proxy> _pool = new();
    private readonly List<CharacterPresenter> _alive = new();
    private readonly CompositeDisposable _disposables = new();
    private bool _open;

    public bool IsOpen => _open;

    /// <summary>canStay: 開いたままでよいか（配信中・カード操作可など）。false になったら閉じる。</summary>
    public void Initialize(BattleSequencer sequencer, Func<bool> canStay)
    {
        _sequencer = sequencer;
        _canStay = canStay;
    }

    private void Awake()
    {
        ControllerInput.IsNavigating
            .Where(on => !on)
            .Subscribe(_ => Close())
            .AddTo(_disposables);
    }

    private void OnDestroy()
    {
        _disposables.Dispose(); // プロキシ用 Canvas は自分の子なので一緒に破棄される
    }

    private void OnDisable() => Close();

    /// <summary>
    /// 選択を開く。生きている敵が minEnemies 体未満なら開かずに false。
    /// 最初は現在の指定（SelectedTarget）、無ければ先頭の敵にフォーカス。
    /// </summary>
    public bool Open(int minEnemies = 1)
    {
        if (_open || _sequencer == null || !ControllerInput.IsNavigating.CurrentValue) return false;
        CollectAlive();
        if (_alive.Count < Mathf.Max(1, minEnemies)) return false;

        EnsureRoot();
        _open = true;
        SyncProxies();

        var ctx = _sequencer.Context;
        var current = ctx != null ? ctx.SelectedTarget.Value : null;
        Proxy first = current != null && _proxies.TryGetValue(current, out var p) ? p : (_alive.Count > 0 ? _proxies[_alive[0]] : null);
        _scope.FirstSelected = first != null ? first.Selectable : null;
        _root.gameObject.SetActive(true); // スコープ登録 → 最前面のモーダルになる
        _scope.Refocus(resetToFirst: true);
        if (first != null) _scope.Select(first.Selectable);
        return true;
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        if (_root != null) _root.gameObject.SetActive(false); // スコープが外れ、元の画面の最後の選択に戻る
    }

    private void LateUpdate()
    {
        if (!_open) return;
        if (_canStay != null && !_canStay())
        {
            Close();
            return;
        }
        CollectAlive();
        if (_alive.Count == 0)
        {
            Close();
            return;
        }
        SyncProxies();
    }

    // ---------------------------------------------------------------- 内部

    private void CollectAlive()
    {
        _alive.Clear();
        var ctx = _sequencer != null ? _sequencer.Context : null;
        if (ctx == null) return;
        foreach (var p in ctx.EnemyPresenters)
        {
            var m = p?.GetModel();
            var v = p?.GetView();
            if (m == null || m.IsDead || v == null || !v.isActiveAndEnabled) continue;
            _alive.Add(p);
        }
    }

    private void EnsureRoot()
    {
        if (_root != null) return;
        var canvasGo = new GameObject("[PadTargetSelect]", typeof(RectTransform), typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000; // 選択枠（32000）より下

        var rootGo = new GameObject("Targets", typeof(RectTransform));
        rootGo.SetActive(false);
        rootGo.transform.SetParent(canvasGo.transform, false);
        _root = (RectTransform)rootGo.transform;
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = _root.offsetMax = Vector2.zero;

        _scope = rootGo.AddComponent<UIFocusScope>();
        _scope.Modal = true;
        _scope.Priority = ScopePriority;
        _scope.RememberLastSelected = false;
        _scope.SetCancelAction(Close);
    }

    private void SyncProxies()
    {
        // 倒れた敵のプロキシを戻す
        var gone = ListPool;
        gone.Clear();
        foreach (var kv in _proxies)
            if (!_alive.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (var key in gone)
        {
            var px = _proxies[key];
            _proxies.Remove(key);
            px.Presenter = null;
            px.gameObject.SetActive(false);
            _pool.Push(px);
        }

        foreach (var p in _alive)
        {
            if (!_proxies.TryGetValue(p, out var px))
            {
                px = _pool.Count > 0 ? _pool.Pop() : CreateProxy();
                px.Presenter = p;
                px.gameObject.SetActive(true);
                _proxies[p] = px;
            }
            Place(px, p.GetView());
        }

        // 画面の左→右に並べて、左右（上下も）で巡回できるようにする
        _ordered.Clear();
        _ordered.AddRange(_proxies.Values);
        _ordered.Sort((a, b) => a.Rect.position.x.CompareTo(b.Rect.position.x));
        int n = _ordered.Count;
        for (int i = 0; i < n; i++)
        {
            var prev = _ordered[(i - 1 + n) % n].Selectable;
            var next = _ordered[(i + 1) % n].Selectable;
            var nav = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = prev,
                selectOnRight = next,
                selectOnUp = prev,
                selectOnDown = next,
            };
            _ordered[i].Selectable.navigation = nav;
        }
        if (_scope != null && n > 0 && (_scope.FirstSelected == null || !_scope.FirstSelected.gameObject.activeInHierarchy))
            _scope.FirstSelected = _ordered[0].Selectable;
    }

    private static readonly List<CharacterPresenter> ListPool = new();
    // Place は開いている間毎フレーム呼ばれるので、GetComponentsInChildren の配列を作らずに済ませる
    private static readonly List<SpriteRenderer> RendererBuffer = new();

    private Proxy CreateProxy()
    {
        var go = new GameObject("Target", typeof(RectTransform));
        go.transform.SetParent(_root, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        var sel = go.AddComponent<Selectable>();
        sel.transition = Selectable.Transition.None;
        var px = go.AddComponent<Proxy>();
        px.Rect = rt;
        px.Selectable = sel;
        px.Owner = this;
        var ov = go.AddComponent<UIFocusHighlightOverride>();
        ov.padding = new Vector2(10f, 10f);
        return px;
    }

    /// <summary>敵スプライトの画面上の矩形にプロキシを合わせる（Overlay Canvas はスクリーン px = ワールド座標）。</summary>
    private static void Place(Proxy px, CharacterView view)
    {
        if (view == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        bool any = false;
        Bounds b = default;
        view.GetComponentsInChildren(false, RendererBuffer);
        foreach (var r in RendererBuffer)
        {
            if (!r.enabled || r.sprite == null) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        RendererBuffer.Clear();
        if (!any)
        {
            var col = view.GetComponentInChildren<Collider2D>();
            if (col == null || !col.enabled) return;
            b = col.bounds;
        }

        Vector2 min = cam.WorldToScreenPoint(b.min);
        Vector2 max = cam.WorldToScreenPoint(b.max);
        var size = new Vector2(Mathf.Max(MinProxySize, Mathf.Abs(max.x - min.x)), Mathf.Max(MinProxySize, Mathf.Abs(max.y - min.y)));
        var center = (min + max) * 0.5f;
        var scale = px.Rect.lossyScale;
        px.Rect.position = new Vector3(center.x, center.y, 0f);
        px.Rect.sizeDelta = new Vector2(size.x / Mathf.Max(0.0001f, scale.x), size.y / Mathf.Max(0.0001f, scale.y));
    }

    private void Submit(Proxy px)
    {
        var presenter = px != null ? px.Presenter : null;
        if (presenter == null) return;
        // タップ時と同じ経路（CharacterView.OnClicked → CharacterPresenter → BattleSequencer.OnCharacterClicked）
        var view = presenter.GetView();
        if (view != null) view.OnClicked.OnNext(Unit.Default);
        Close();
    }

    private sealed class Proxy : MonoBehaviour, ISubmitHandler
    {
        public RectTransform Rect;
        public Selectable Selectable;
        public CharacterPresenter Presenter;
        public BattlePadTargetSelector Owner;

        public void OnSubmit(BaseEventData eventData)
        {
            if (Owner != null) Owner.Submit(this);
        }
    }
}
