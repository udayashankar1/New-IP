using UnityEngine;

/// <summary>
/// Stateless helpers shared across the stealth systems. Centralises a couple of
/// geometry routines that used to be copy-pasted between the interaction prompts
/// and the enemy AI, so the behaviour stays identical everywhere and is fixed in
/// one place.
/// </summary>
public static class StealthUtils
{
    /// <summary>
    /// True if anything solid sits between <paramref name="cam"/> and
    /// <paramref name="targetPoint"/> — i.e. the candidate is hidden behind another
    /// body or a wall. The candidate's own colliders and the player are ignored so
    /// they never count as occluders.
    /// </summary>
    public static bool IsOccluded(Camera cam, Vector3 targetPoint, Transform self)
    {
        if (cam == null) return false;

        Vector3 from = cam.transform.position;
        Vector3 dir  = targetPoint - from;
        float   dist = dir.magnitude;
        if (dist < 0.05f) return false;

        foreach (var h in Physics.RaycastAll(from, dir / dist, dist - 0.1f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform == self || h.collider.transform.IsChildOf(self)) continue;
            if (h.collider.GetComponentInParent<PlayerController>() != null)           continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Smoothly rotate <paramref name="t"/> about its Y axis to face a world point
    /// (height ignored). No-op when the point is effectively on top of the transform.
    /// </summary>
    public static void FaceWorldPoint(Transform t, Vector3 worldPoint, ref float yawVelocity,
                                      float smoothTime, float maxTurnSpeed)
    {
        Vector3 dir = worldPoint - t.position;
        dir.y = 0f;
        if (dir.sqrMagnitude <= 0.001f) return;

        float targetYaw = Quaternion.LookRotation(dir.normalized).eulerAngles.y;
        float newYaw    = Mathf.SmoothDampAngle(t.eulerAngles.y, targetYaw,
                                                ref yawVelocity, smoothTime, maxTurnSpeed);
        t.rotation = Quaternion.Euler(0f, newYaw, 0f);
    }
}
