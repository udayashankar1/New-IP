using System.Collections;
using UnityEngine;

[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(CharacterController))]
public class StealthTakedown : MonoBehaviour
{
    [Header("Detection")]
    public float takedownRange = 2f;
    [Tooltip("Player must be within this many degrees of the enemy's back")]
    public float behindDegrees = 60f;

    [Header("Approach")]
    public float approachSpeed    = 2.5f;
    public float rotateSpeed      = 360f;
    public float arrivalThreshold = 0.12f;
    [Tooltip("Fallback offset behind enemy if no TakedownPoint is assigned")]
    public float snapOffset       = 0.65f;

    [Header("Adjustment")]
    [Tooltip("ON: player becomes a child of the takedown point (moves with it). OFF: player snaps to the position and plays independently.")]
    public bool attachToPoint = false;

    PlayerController    _player;
    Animator            _anim;
    CharacterController _cc;
    bool                _busy;

    static readonly int HashVelZ = Animator.StringToHash("VelocityZ");

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _anim   = GetComponent<Animator>();
        _cc     = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (_busy || !_player.IsCrouching || !Input.GetKeyDown(KeyCode.F)) return;
        EnemyPatrol target = FindTarget();
        if (target != null) StartCoroutine(Execute(target));
    }

    EnemyPatrol FindTarget()
    {
        foreach (var col in Physics.OverlapSphere(transform.position, takedownRange))
        {
            var e = col.GetComponent<EnemyPatrol>() ?? col.GetComponentInParent<EnemyPatrol>();
            if (e == null || !e.CanBeTakenDown) continue;
            Vector3 toPlayer = transform.position - e.transform.position;
            toPlayer.y = 0f;
            if (Vector3.Angle(e.transform.forward, toPlayer.normalized) >= 180f - behindDegrees)
                return e;
        }
        return null;
    }

    IEnumerator Execute(EnemyPatrol enemy)
    {
        _busy = true;
        _player.LockControls();

        // Disable enemy colliders so CC can reach the takedown point
        foreach (var col in enemy.GetComponentsInChildren<Collider>())
            col.enabled = false;

        // Switch both animators to unscaled time so takedown works when paused
        Animator enemyAnim = enemy.GetComponent<Animator>();
        var prevPlayerMode = _anim.updateMode;
        var prevEnemyMode  = enemyAnim != null ? enemyAnim.updateMode : AnimatorUpdateMode.Normal;
        _anim.updateMode = AnimatorUpdateMode.UnscaledTime;
        if (enemyAnim != null) enemyAnim.updateMode = AnimatorUpdateMode.UnscaledTime;

        // ── Smooth approach (unscaled so it works while paused) ──
        Transform point = enemy.takedownPoint;

        while (true)
        {
            Vector3 dest = GetApproachDest(enemy, point);
            Vector3 flat = dest - transform.position;
            flat.y = 0f;

            if (flat.magnitude <= arrivalThreshold) break;

            _cc.Move(flat.normalized * Mathf.Min(approachSpeed * Time.unscaledDeltaTime, flat.magnitude));

            Quaternion targetRot = point != null ? point.rotation : enemy.transform.rotation;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, rotateSpeed * Time.unscaledDeltaTime);

            _anim.SetFloat(HashVelZ, 0.4f);
            yield return null;
        }

        // ── Snap to exact editor-placed position ──
        Vector3 finalPos = point != null
            ? point.position
            : enemy.transform.position - enemy.transform.forward * snapOffset;
        Quaternion finalRot = point != null ? point.rotation : enemy.transform.rotation;

        _cc.enabled = false;
        transform.SetPositionAndRotation(finalPos, finalRot);
        _anim.SetFloat(HashVelZ, 0f);

        // ── Optionally parent player to takedown point ──
        Transform originalParent = transform.parent;
        if (attachToPoint)
        {
            Transform attachTo = point != null ? point : enemy.transform;
            transform.SetParent(attachTo, worldPositionStays: true);
        }

        // ── Trigger both animations simultaneously ──
        _anim.CrossFadeInFixedTime("Assassination", 0.05f);
        enemy.BeginTakedown();

        yield return null; // let CrossFade register

        // Wait to enter Assassination state
        float wait = 0f;
        while (wait < 2f)
        {
            wait += Time.unscaledDeltaTime;
            if (_anim.GetCurrentAnimatorStateInfo(0).IsName("Assassination") ||
                _anim.GetNextAnimatorStateInfo(0).IsName("Assassination")) break;
            yield return null;
        }

        // Wait for animation to finish
        wait = 0f;
        while (wait < 10f)
        {
            wait += Time.unscaledDeltaTime;
            var info = _anim.GetCurrentAnimatorStateInfo(0);
            if (info.IsName("Assassination") && info.normalizedTime >= 0.88f) break;
            yield return null;
        }

        // ── Cleanup ──
        if (attachToPoint)
            transform.SetParent(originalParent, worldPositionStays: true);
        _cc.enabled = true;

        _anim.updateMode = prevPlayerMode;
        if (enemyAnim != null) enemyAnim.updateMode = prevEnemyMode;

        _anim.CrossFadeInFixedTime("CrouchLocomotion", 0.5f);
        _player.UnlockControls();
        _busy = false;
    }

    // Approach destination: exact XZ of the point, current player Y (gravity handles vertical during walk-in)
    Vector3 GetApproachDest(EnemyPatrol enemy, Transform point)
    {
        Vector3 dest = point != null
            ? point.position
            : enemy.transform.position - enemy.transform.forward * snapOffset;
        dest.y = transform.position.y;
        return dest;
    }
}
