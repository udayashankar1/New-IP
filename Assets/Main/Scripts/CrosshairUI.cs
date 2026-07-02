using UnityEngine;

/// <summary>
/// Shows a centre-screen crosshair (the white dot) only while the player is aiming.
/// Lives on the crosshair Canvas; toggles the assigned graphic with
/// <see cref="PlayerController.IsAiming"/>.
/// </summary>
public class CrosshairUI : MonoBehaviour
{
    [Tooltip("The crosshair graphic (white dot). Defaults to the first child if left empty.")]
    public GameObject crosshair;

    void Awake()
    {
        if (crosshair == null && transform.childCount > 0)
            crosshair = transform.GetChild(0).gameObject;
        if (crosshair != null) crosshair.SetActive(false);
    }

    void Update()
    {
        if (crosshair == null) return;
        var player = GameManager.Instance.Player;
        bool aiming = player != null && player.IsAiming;
        if (crosshair.activeSelf != aiming) crosshair.SetActive(aiming);
    }
}
