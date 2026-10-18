using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class PlayerMove : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    private readonly ReactiveProperty<Vector2> moveInput = new();
    [SerializeField] private Rigidbody2D rb;

    [SerializeField] private PlayerInput playerInput;

    [SerializeField] private Animator animator;

    [Header("パッド/キー操作の分離（店ホーム用。村は OFF のまま）")]
    [Tooltip("ON: 移動はスティックと WASD のみ。矢印キー・十字キーは無視する（UI の選択移動に回す）。")]
    [SerializeField] private bool ignoreArrowKeysAndDpad;
    [Tooltip("ON: 親の UIFocusScope 以外のスコープ（パネル・ポップアップ）が最前面の間は歩かない。")]
    [SerializeField] private bool walkOnlyWhileParentScopeTop;

    private CompositeDisposable moveDisposables;

    private Vector3 playerStartScale;
    private Vector2 callbackInput;
    private UIFocusScope parentScope;

    private void Start()
    {
        playerStartScale = transform.localScale;
        if (walkOnlyWhileParentScopeTop)
            parentScope = GetComponentInParent<UIFocusScope>(true);
        moveDisposables = new CompositeDisposable();
        moveInput
            .Subscribe(x => animator.SetBool("1_Move", x != Vector2.zero))
            .AddTo(moveDisposables);
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        callbackInput = context.ReadValue<Vector2>();
        if (!ignoreArrowKeysAndDpad && !walkOnlyWhileParentScopeTop)
            moveInput.Value = callbackInput;
    }

    private void Update()
    {
        if (!ignoreArrowKeysAndDpad && !walkOnlyWhileParentScopeTop) return;

        Vector2 v = ignoreArrowKeysAndDpad ? ReadMoveExcludingArrowsAndDpad() : callbackInput;
        if (walkOnlyWhileParentScopeTop && !CanWalk()) v = Vector2.zero;
        moveInput.Value = v;
    }

    private bool CanWalk()
    {
        var top = UIFocusManager.Top;
        return top == null || top == parentScope;
    }

    /// <summary>
    /// Move アクションの現在の入力を、矢印キー・十字キー由来を除いて読み直す
    /// （コンポジット 2DVector のキーは up/down/left/right のパート名で方向を決める）。
    /// </summary>
    private Vector2 ReadMoveExcludingArrowsAndDpad()
    {
        var actions = playerInput != null ? playerInput.actions : null;
        var action = actions != null ? actions.FindAction("Move") : null;
        if (action == null || !action.enabled) return Vector2.zero;

        Vector2 analog = Vector2.zero;
        Vector2 digital = Vector2.zero;
        var bindings = action.bindings;
        foreach (var control in action.controls)
        {
            if (IsArrowOrDpad(control)) continue;
            if (control is Vector2Control v2)
            {
                var value = v2.ReadValue();
                if (value.sqrMagnitude > analog.sqrMagnitude) analog = value;
                continue;
            }
            if (control is ButtonControl button && button.isPressed)
            {
                int index = action.GetBindingIndexForControl(control);
                if (index < 0) continue;
                switch (bindings[index].name)
                {
                    case "up": digital.y += 1f; break;
                    case "down": digital.y -= 1f; break;
                    case "left": digital.x -= 1f; break;
                    case "right": digital.x += 1f; break;
                }
            }
        }
        digital.x = Mathf.Clamp(digital.x, -1f, 1f);
        digital.y = Mathf.Clamp(digital.y, -1f, 1f);
        if (digital != Vector2.zero) digital.Normalize();
        return digital.sqrMagnitude >= analog.sqrMagnitude ? digital : analog;
    }

    private static bool IsArrowOrDpad(InputControl control)
    {
        if (control is KeyControl key)
        {
            var k = key.keyCode;
            return k == Key.UpArrow || k == Key.DownArrow || k == Key.LeftArrow || k == Key.RightArrow;
        }
        // 十字キー（Gamepad.dpad とその子）・ジョイスティックのハット
        for (var c = control; c != null; c = c.parent)
            if (c is DpadControl) return true;
        return false;
    }

    private void FixedUpdate()
    {
        rb.MovePosition(rb.position + moveInput.Value * moveSpeed * Time.fixedDeltaTime);

        // 向き変更処理
        if (moveInput.Value.x > 0)
        {
            // 右向き
            transform.localScale = new Vector3(-playerStartScale.x ,playerStartScale.y, playerStartScale.z);
        }
        else if (moveInput.Value.x < 0)
        {
            // 左向き（X軸反転）
            transform.localScale = new Vector3(playerStartScale.x, playerStartScale.y, playerStartScale.z);
        }
    }

    private void OnDestroy() => moveDisposables?.Dispose();
}
