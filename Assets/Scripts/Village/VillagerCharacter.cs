using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 歩ける村の住民（企画書 §13-F）。
/// 家（初期位置）の周囲をランダムに歩く → 数秒立ち止まる → また歩く、のループのみ。
/// プレイヤー（PlayerMove）が近づくと足を止めて振り向き、一言しゃべる（吹き出しはワールドTMP）。
/// 世話・欲求・スケジュールは持たない（村人=発展の擬人化に徹する）。
/// 見た目は SPUM ユニット（子オブジェクト）。CustomerCharacter と同じく PlayAnimation と localScale.x 反転で制御。
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class VillagerCharacter : MonoBehaviour
{
    [Header("見た目")]
    [SerializeField] private SPUM_Prefabs spumCharacter;
    [Tooltip("非表示にするパーツ（武器・盾など）のTransform名。村人らしく手ぶらにしたいとき用")]
    [SerializeField] private string[] hiddenPartNames = { };

    [Header("徘徊")]
    [Tooltip("false なら徘徊せずその場で待機（見張り・店番など）")]
    [SerializeField] private bool wander = true;
    [Tooltip("家（開始位置）からの徘徊半径")]
    [SerializeField] private float wanderRadius = 4f;
    [SerializeField] private float walkSpeed = 1.3f;
    [SerializeField] private Vector2 idleSecondsRange = new(2f, 5f);
    [Tooltip("設定時はこのTilemapにタイルがある場所（道）だけを目的地にする")]
    [SerializeField] private Tilemap walkableTilemap;
    [SerializeField] private float bobAmplitude = 0.04f;
    [SerializeField] private float bobFrequency = 3f;

    [Header("話しかけ")]
    [SerializeField] private GameObject lineBubble;
    [SerializeField] private TMP_Text lineText;
    [TextArea] [SerializeField] private string[] lines = { };

    private Vector3 home;
    private Vector3 target;
    private Vector3 unitBaseLocalPos;
    private Vector3 unitBaseScale;
    private float idleTimer;
    private float walkElapsed;
    private bool walking;
    private bool talking;
    private Transform talkTarget;
    private int lineIndex;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
    private ContactFilter2D solidFilter;

    private void Awake()
    {
        home = transform.position;
        solidFilter = new ContactFilter2D { useTriggers = false };
        solidFilter.NoFilter();
        solidFilter.useTriggers = false;

        if (spumCharacter != null)
        {
            unitBaseLocalPos = spumCharacter.transform.localPosition;
            unitBaseScale = spumCharacter.transform.localScale;
            spumCharacter.OverrideControllerInit();
            HideParts();
        }
        if (lineBubble != null) lineBubble.SetActive(false);
        lineIndex = lines != null && lines.Length > 0 ? Random.Range(0, lines.Length) : 0;
        idleTimer = Random.Range(0.3f, idleSecondsRange.y);
    }

    private void Start()
    {
        PlayState(PlayerState.IDLE);
        if (spumCharacter != null && Random.value < 0.5f) Face(transform.position + Vector3.right);
    }

    private void HideParts()
    {
        if (hiddenPartNames == null || hiddenPartNames.Length == 0) return;
        foreach (var t in spumCharacter.GetComponentsInChildren<Transform>(true))
        {
            foreach (var n in hiddenPartNames)
            {
                if (!string.IsNullOrEmpty(n) && t.name == n)
                {
                    foreach (var sr in t.GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = false;
                }
            }
        }
    }

    private void Update()
    {
        if (talking)
        {
            if (talkTarget != null) Face(talkTarget.position);
            return;
        }

        if (walking)
        {
            StepWalk();
            return;
        }

        idleTimer -= Time.deltaTime;
        if (idleTimer > 0f) return;

        if (wander && TryPickTarget(out target))
        {
            walking = true;
            walkElapsed = 0f;
            Face(target);
            PlayState(PlayerState.MOVE);
        }
        else
        {
            // 歩けない/待機型: きょろきょろ向きを変えるだけ
            if (spumCharacter != null) Face(transform.position + (Random.value < 0.5f ? Vector3.left : Vector3.right));
            idleTimer = Random.Range(idleSecondsRange.x, idleSecondsRange.y);
        }
    }

    private void StepWalk()
    {
        walkElapsed += Time.deltaTime;
        Vector3 pos = Vector3.MoveTowards(transform.position, target, walkSpeed * Time.deltaTime);
        transform.position = pos;

        if (spumCharacter != null)
        {
            float bob = Mathf.Abs(Mathf.Sin(walkElapsed * bobFrequency * Mathf.PI)) * bobAmplitude;
            spumCharacter.transform.localPosition = unitBaseLocalPos + new Vector3(0f, bob, 0f);
        }

        if ((pos - target).sqrMagnitude < 0.0004f) StopWalking();
    }

    private void StopWalking()
    {
        walking = false;
        if (spumCharacter != null) spumCharacter.transform.localPosition = unitBaseLocalPos;
        PlayState(PlayerState.IDLE);
        idleTimer = Random.Range(idleSecondsRange.x, idleSecondsRange.y);
    }

    /// <summary>家の周囲で、道の上かつ障害物（非Triggerコライダー）に遮られない地点を探す。</summary>
    private bool TryPickTarget(out Vector3 result)
    {
        for (int i = 0; i < 12; i++)
        {
            Vector2 p = (Vector2)home + Random.insideUnitCircle * wanderRadius;
            Vector2 from = transform.position;
            Vector2 delta = p - from;
            if (delta.magnitude < 1f) continue;

            if (walkableTilemap != null && !walkableTilemap.HasTile(walkableTilemap.WorldToCell(p))) continue;

            int n = Physics2D.CircleCast(from, 0.25f, delta.normalized, solidFilter, castHits, delta.magnitude + 0.3f);
            bool blocked = false;
            for (int h = 0; h < n; h++)
            {
                var col = castHits[h].collider;
                if (col == null || col.transform.IsChildOf(transform)) continue;
                blocked = true;
                break;
            }
            if (blocked) continue;

            result = new Vector3(p.x, p.y, transform.position.z);
            return true;
        }
        result = transform.position;
        return false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var player = other.GetComponentInParent<PlayerMove>();
        if (player == null) return;

        talking = true;
        talkTarget = player.transform;
        if (walking)
        {
            walking = false;
            if (spumCharacter != null) spumCharacter.transform.localPosition = unitBaseLocalPos;
        }
        PlayState(PlayerState.IDLE);
        Face(talkTarget.position);
        ShowLine();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerMove>() == null) return;
        talking = false;
        talkTarget = null;
        HideLine();
        idleTimer = Random.Range(idleSecondsRange.x, idleSecondsRange.y);
    }

    private void ShowLine()
    {
        if (lineBubble == null || lines == null || lines.Length == 0) return;
        if (lineText != null) lineText.text = lines[lineIndex % lines.Length];
        lineIndex = (lineIndex + 1) % lines.Length;

        var t = lineBubble.transform;
        t.DOKill();
        lineBubble.SetActive(true);
        t.localScale = Vector3.one * 0.5f;
        t.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetLink(lineBubble);
    }

    private void HideLine()
    {
        if (lineBubble == null || !lineBubble.activeSelf) return;
        var t = lineBubble.transform;
        t.DOKill();
        t.DOScale(0f, 0.15f).SetEase(Ease.InBack).SetLink(lineBubble)
            .OnComplete(() => { if (lineBubble != null) lineBubble.SetActive(false); });
    }

    private void PlayState(PlayerState state)
    {
        if (spumCharacter == null || !spumCharacter.allListsHaveItemsExist()) return;
        spumCharacter.PlayAnimation(state, 0);
    }

    /// <summary>右向き → X を負（PlayerMove / CustomerCharacter と同じ規則）。</summary>
    private void Face(Vector3 point)
    {
        if (spumCharacter == null) return;
        float dx = point.x - transform.position.x;
        if (Mathf.Abs(dx) < 0.05f) return;
        Vector3 s = unitBaseScale;
        spumCharacter.transform.localScale = dx > 0
            ? new Vector3(-Mathf.Abs(s.x), s.y, s.z)
            : new Vector3(Mathf.Abs(s.x), s.y, s.z);
    }

    private void OnDestroy()
    {
        if (lineBubble != null) lineBubble.transform.DOKill();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.9f, 0.4f, 0.6f);
        Gizmos.DrawWireSphere(Application.isPlaying ? home : transform.position, wanderRadius);
    }
#endif
}
