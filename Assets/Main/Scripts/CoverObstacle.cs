using UnityEngine;

/// <summary>
/// Marker for a collider the player can take cover behind. Put it on any box/wall.
/// <see cref="CoverController"/> only treats colliders carrying this component as cover,
/// and reads the collider's top height to decide crouched vs standing cover.
/// </summary>
public class CoverObstacle : MonoBehaviour { }
