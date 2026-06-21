using UnityEngine;

/// <summary>
/// Player-side scanner for enemies that can be lured. Shows a pooled Q prompt over
/// the enemy nearest screen centre; pressing Q summons the Ghost behind it (see
/// <see cref="EnemyPatrol.LureFromBehind"/>).
///
/// All the scanning / single-prompt arbitration lives in <see cref="PlayerInteractor{T}"/>.
/// This class only declares the enemy-specific policy. It loses exact angle ties to
/// <see cref="DistractionInteractor"/>, so a droppable object in the same spot wins.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class EnemyInteractionPrompt : PlayerInteractor<EnemyPatrol>
{
    protected override bool WinsAngleTie => false;   // distractions win exact ties

    protected override bool CanInteract(EnemyPatrol e) => e.CanBeTakenDown && !e.IsFPromptActive;
    protected override void ShowPrompt(EnemyPatrol e, bool show) => e.ShowQPrompt(show);
    protected override void Activate(EnemyPatrol e) => e.LureFromBehind();
}
